function Publish-PM365RuntimeArtifacts {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Config
    )

    $release = $Config.runtimeArtifacts
    $resourceGroupName = [string]$Config.azure.resourceGroupName
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "pm365-runtime-$([guid]::NewGuid().ToString('N'))"
    $evidence = [ordered]@{
        status = 'Failed'
        releaseId = [string]$release.releaseId
        runtimeVersion = [string]$release.runtimeVersion
        artifacts = @()
        error = $null
    }

    try {
        New-Item -Path $tempRoot -ItemType Directory -Force | Out-Null
        $definitions = @(
            [pscustomobject]@{
                kind = 'api'
                targetAppName = [string]$Config.azure.resourceNames.apiAppName
                contract = $release.api
            },
            [pscustomobject]@{
                kind = 'portal'
                targetAppName = [string]$Config.azure.resourceNames.portalAppName
                contract = $release.portal
            }
        )

        $allowLocalDevelopment = [string]$Config.azure.environment -ceq 'dev' -and
            [Environment]::GetEnvironmentVariable('PM365_ALLOW_LOCAL_RUNTIME_ARTIFACTS', 'Process') -ceq 'true'
        $apiArtifactUri = Get-PM365TrustedRuntimeArtifactUri `
            -Value ([string]$release.api.downloadUrl) `
            -AllowLocalDevelopment:$allowLocalDevelopment
        $portalArtifactUri = Get-PM365TrustedRuntimeArtifactUri `
            -Value ([string]$release.portal.downloadUrl) `
            -AllowLocalDevelopment:$allowLocalDevelopment
        if ([uri]::new($apiArtifactUri, '.').AbsoluteUri -cne [uri]::new($portalArtifactUri, '.').AbsoluteUri) {
            throw [System.IO.InvalidDataException]::new('RuntimeArtifactContractInvalid:runtime:downloadUrl')
        }

        $validatedArtifacts = @()
        foreach ($definition in $definitions) {
            $artifact = $definition.contract
            $expectedStartupCommand = if ($definition.kind -eq 'api') {
                'node dist/index.js'
            } else {
                'node .pm365/start-portal-runtime.mjs'
            }
            if ([string]$artifact.startupCommand -cne $expectedStartupCommand) {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactContractInvalid:$($definition.kind):startupCommand")
            }
            $uri = Get-PM365TrustedRuntimeArtifactUri `
                -Value ([string]$artifact.downloadUrl) `
                -AllowLocalDevelopment:$allowLocalDevelopment
            $fileName = [string]$artifact.fileName
            if ([System.IO.Path]::GetFileName($fileName) -ne $fileName -or $fileName -cnotmatch '^[A-Za-z0-9._+-]+\.zip$') {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactContractInvalid:$($definition.kind):fileName")
            }
            if ([uri]::UnescapeDataString($uri.Segments[-1]) -cne $fileName) {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactContractInvalid:$($definition.kind):downloadUrl")
            }

            $declaredHash = [string]$artifact.sha256
            if ($declaredHash -cnotmatch '^[0-9a-f]{64}$') {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactContractInvalid:$($definition.kind):sha256")
            }

            $archivePath = Join-Path $tempRoot "$($definition.kind)-$fileName"
            try {
                Save-PM365RuntimeArtifactDownload -Uri $uri -OutputPath $archivePath -MaximumBytes 268435456
            } catch {
                throw [System.IO.IOException]::new("RuntimeArtifactDownloadFailed:$($definition.kind)")
            }

            $file = Get-Item -LiteralPath $archivePath -ErrorAction Stop
            $declaredSize = [long]$artifact.sizeBytes
            if ($declaredSize -le 0 -or
                $declaredSize -gt 268435456 -or
                $file.Length -cne $declaredSize) {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactSizeInvalid:$($definition.kind)")
            }

            $computedHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($computedHash -cne $declaredHash) {
                throw [System.Security.Cryptography.CryptographicException]::new("RuntimeArtifactIntegrityFailed:$($definition.kind)")
            }

            Test-PM365RuntimeArtifactArchive `
                -ArchivePath $archivePath `
                -Kind $definition.kind `
                -ReleaseId ([string]$release.releaseId) `
                -RuntimeVersion ([string]$release.runtimeVersion) `
                -SourceCommit ([string]$release.sourceCommit) `
                -StartupCommand $expectedStartupCommand

            $validatedArtifacts += [pscustomobject]@{
                kind = $definition.kind
                targetAppName = $definition.targetAppName
                fileName = $fileName
                uri = $uri
                file = $file
                declaredHash = $declaredHash
                computedHash = $computedHash
                declaredSize = $declaredSize
            }
        }

        foreach ($artifact in $validatedArtifacts) {
            try {
                Publish-AzWebApp `
                    -ResourceGroupName $resourceGroupName `
                    -Name $artifact.targetAppName `
                    -ArchivePath $artifact.file.FullName `
                    -Force `
                    -ErrorAction Stop | Out-Null
            } catch {
                throw [System.InvalidOperationException]::new("RuntimeArtifactPublishFailed:$($artifact.kind)")
            }

            $evidence.artifacts += [pscustomobject][ordered]@{
                kind = $artifact.kind
                fileName = $artifact.fileName
                sourceHost = $artifact.uri.Host
                byteCount = $artifact.file.Length
                declaredSha256 = $artifact.declaredHash
                computedSha256 = $artifact.computedHash
                targetAppName = $artifact.targetAppName
                status = 'Passed'
            }
        }

        $evidence.status = 'Passed'
        [pscustomobject]$evidence
    } catch {
        $parts = [string]$_.Exception.Message -split ':'
        $code = if ($parts.Count -gt 0 -and $parts[0] -match '^RuntimeArtifact[A-Za-z]+$') {
            $parts[0]
        } else {
            'RuntimeArtifactDeploymentFailed'
        }
        $kind = if ($parts.Count -gt 1 -and $parts[1] -in @('api', 'portal')) { $parts[1] } else { 'runtime' }
        $evidence.error = [ordered]@{
            code = $code
            artifactKind = $kind
            message = "The verified $kind runtime artifact could not be deployed."
        }
        [pscustomobject]$evidence
    } finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Save-PM365RuntimeArtifactDownload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [uri] $Uri,

        [Parameter(Mandatory)]
        [string] $OutputPath,

        [Parameter(Mandatory)]
        [long] $MaximumBytes
    )

    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [timespan]::FromMinutes(5)
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $Uri)
    $response = $null
    $inputStream = $null
    $outputStream = $null
    try {
        $response = $client.SendAsync(
            $request,
            [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            throw [System.Net.Http.HttpRequestException]::new("Runtime artifact download returned HTTP $([int]$response.StatusCode).")
        }

        if ($response.Content.Headers.ContentLength.HasValue -and
            $response.Content.Headers.ContentLength.Value -gt $MaximumBytes) {
            throw [System.IO.InvalidDataException]::new('Runtime artifact exceeded the maximum download size.')
        }

        $inputStream = $response.Content.ReadAsStream()
        $outputStream = [System.IO.File]::Create($OutputPath)
        $buffer = [byte[]]::new(81920)
        $totalBytes = [long]0
        while (($read = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $totalBytes += $read
            if ($totalBytes -gt $MaximumBytes) {
                throw [System.IO.InvalidDataException]::new('Runtime artifact exceeded the maximum download size.')
            }
            $outputStream.Write($buffer, 0, $read)
        }
    } finally {
        $outputStream?.Dispose()
        $inputStream?.Dispose()
        $response?.Dispose()
        $request.Dispose()
        $client.Dispose()
        $handler.Dispose()
    }
}

function Get-PM365TrustedRuntimeArtifactUri {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Value,

        [switch] $AllowLocalDevelopment
    )

    if ($Value -cne $Value.Trim()) {
        throw [System.IO.InvalidDataException]::new('RuntimeArtifactContractInvalid:runtime:downloadUrl')
    }

    $uri = $null
    if (-not [uri]::TryCreate($Value, [System.UriKind]::Absolute, [ref]$uri)) {
        throw [System.IO.InvalidDataException]::new('RuntimeArtifactContractInvalid:runtime:downloadUrl')
    }

    $isLocal = $uri.Host -in @('localhost', '127.0.0.1', '::1')
    $allowedHost = $uri.Host -in @('downloads.pagemaker365.com', 'downloads-staging.pagemaker365.com')
    if ((-not $allowedHost -and -not ($isLocal -and $AllowLocalDevelopment)) -or
        ($uri.Scheme -ne 'https' -and -not ($isLocal -and $AllowLocalDevelopment -and $uri.Scheme -eq 'http')) -or
        (-not $isLocal -and -not $uri.IsDefaultPort) -or
        -not [string]::IsNullOrWhiteSpace($uri.UserInfo) -or
        -not [string]::IsNullOrWhiteSpace($uri.Query) -or
        -not [string]::IsNullOrWhiteSpace($uri.Fragment) -or
        $uri.AbsolutePath.Contains('//')) {
        throw [System.IO.InvalidDataException]::new('RuntimeArtifactContractInvalid:runtime:downloadUrl')
    }

    $uri
}

function Test-PM365RuntimeArtifactArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ArchivePath,

        [Parameter(Mandatory)]
        [ValidateSet('api', 'portal')]
        [string] $Kind,

        [Parameter(Mandatory)]
        [string] $ReleaseId,

        [Parameter(Mandatory)]
        [string] $RuntimeVersion,

        [Parameter(Mandatory)]
        [string] $SourceCommit,

        [Parameter(Mandatory)]
        [string] $StartupCommand
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        if ($archive.Entries.Count -eq 0 -or $archive.Entries.Count -gt 100000) {
            throw [System.IO.InvalidDataException]::new("RuntimeArtifactArchiveInvalid:$Kind")
        }

        $totalLength = [long]0
        $entryNames = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::Ordinal)
        foreach ($entry in $archive.Entries) {
            $normalizedName = $entry.FullName.Replace('\', '/')
            if ($entry.FullName.Contains('\') -or
                $normalizedName -cne $entry.FullName -or
                [string]::IsNullOrEmpty($normalizedName) -or
                $normalizedName.StartsWith('/') -or
                $normalizedName -match '//' -or
                $normalizedName -match '(^|/)\.(/|$)' -or
                $normalizedName -match '(^|/)\.\.(/|$)' -or
                [System.IO.Path]::IsPathRooted($entry.FullName)) {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactArchiveInvalid:$Kind")
            }

            if (-not $entryNames.Add($normalizedName)) {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactArchiveInvalid:$Kind")
            }

            $externalAttributes = [System.BitConverter]::ToUInt32(
                [System.BitConverter]::GetBytes([int]$entry.ExternalAttributes),
                0)
            $unixFileType = ($externalAttributes -shr 16) -band 0xF000
            $windowsAttributes = $externalAttributes -band 0xFFFF
            if ($unixFileType -eq 0xA000 -or
                ($windowsAttributes -band [int][System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactArchiveInvalid:$Kind")
            }

            $totalLength += $entry.Length
            if ($totalLength -gt 1073741824) {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactArchiveInvalid:$Kind")
            }
        }

        $requiredEntries = if ($Kind -eq 'api') {
            @('dist/index.js', 'package.json', '.pm365/provenance.json')
        } else {
            @(
                'index.html',
                'auth-redirect.html',
                '.pm365/start-portal-runtime.mjs',
                '.pm365/generate-web-runtime-config.mjs',
                '.pm365/provenance.json'
            )
        }
        foreach ($requiredEntry in $requiredEntries) {
            if (-not ($archive.Entries.FullName.Replace('\', '/') -ccontains $requiredEntry)) {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactArchiveInvalid:$Kind")
            }
        }

        $provenanceEntry = $archive.Entries |
            Where-Object { $_.FullName.Replace('\', '/') -ceq '.pm365/provenance.json' } |
            Select-Object -First 1
        if ($provenanceEntry.Length -le 0 -or $provenanceEntry.Length -gt 65536) {
            throw [System.IO.InvalidDataException]::new("RuntimeArtifactArchiveInvalid:$Kind")
        }

        $provenanceReader = [System.IO.StreamReader]::new($provenanceEntry.Open())
        try {
            $provenanceJson = $provenanceReader.ReadToEnd()
        } finally {
            $provenanceReader.Dispose()
        }

        try {
            $provenance = $provenanceJson | ConvertFrom-Json -Depth 4 -ErrorAction Stop
        } catch {
            throw [System.IO.InvalidDataException]::new("RuntimeArtifactArchiveInvalid:$Kind")
        }
        $expectedProvenancePropertyNames = @(
            'schemaVersion',
            'product',
            'artifactKind',
            'releaseId',
            'runtimeVersion',
            'sourceRepository',
            'sourceCommit',
            'dependencyLockSha256',
            'startupCommand'
        )
        $provenancePropertyNames = @($provenance.PSObject.Properties.Name)
        $nonStringProvenanceProperties = @($expectedProvenancePropertyNames | Where-Object {
            $property = $provenance.PSObject.Properties[$_]
            $null -eq $property -or $property.Value -isnot [string]
        })
        if ($null -eq $provenance -or
            $provenancePropertyNames.Count -ne $expectedProvenancePropertyNames.Count -or
            @($expectedProvenancePropertyNames | Where-Object { -not ($provenancePropertyNames -ccontains $_) }).Count -gt 0 -or
            $nonStringProvenanceProperties.Count -gt 0 -or
            [string]$provenance.schemaVersion -cne 'pagemaker365.runtime-provenance.v1' -or
            [string]$provenance.product -cne 'PageMaker365' -or
            [string]$provenance.artifactKind -cne $Kind -or
            [string]$provenance.releaseId -cne $ReleaseId -or
            [string]$provenance.runtimeVersion -cne $RuntimeVersion -or
            [string]$provenance.sourceRepository -cne 'cloudbossdev/spo-ui' -or
            [string]$provenance.sourceCommit -cne $SourceCommit -or
            [string]$provenance.sourceCommit -cnotmatch '^[0-9a-f]{40}$' -or
            [string]$provenance.dependencyLockSha256 -cnotmatch '^[0-9a-f]{64}$' -or
            [string]$provenance.startupCommand -cne $StartupCommand) {
            throw [System.IO.InvalidDataException]::new("RuntimeArtifactArchiveInvalid:$Kind")
        }

        if ($Kind -eq 'portal') {
            if ($archive.Entries.FullName.Replace('\', '/') -ccontains 'staticwebapp.config.json') {
                throw [System.IO.InvalidDataException]::new('RuntimeArtifactArchiveInvalid:portal')
            }

            $indexEntry = $archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -ceq 'index.html' } | Select-Object -First 1
            if ($indexEntry.Length -gt 5242880) {
                throw [System.IO.InvalidDataException]::new('RuntimeArtifactArchiveInvalid:portal')
            }

            $reader = [System.IO.StreamReader]::new($indexEntry.Open())
            try {
                $indexContent = $reader.ReadToEnd()
            } finally {
                $reader.Dispose()
            }
            $hasProduct = $indexContent -match '(?i)PageMaker365'
            $portalReleaseMarker = Get-PM365PortalReleaseMarker -Content $indexContent
            $hasRelease = [string]::Equals(
                $portalReleaseMarker,
                $ReleaseId,
                [System.StringComparison]::Ordinal)
            if (-not $hasProduct -or -not $hasRelease) {
                throw [System.IO.InvalidDataException]::new('RuntimeArtifactArchiveInvalid:portal')
            }
        }

        $true
    } finally {
        $archive.Dispose()
    }
}
