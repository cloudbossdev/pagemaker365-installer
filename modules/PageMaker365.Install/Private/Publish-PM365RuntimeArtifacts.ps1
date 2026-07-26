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

        foreach ($definition in $definitions) {
            $artifact = $definition.contract
            $expectedStartupCommand = if ($definition.kind -eq 'api') {
                'node dist/index.js'
            } else {
                'pm2 serve /home/site/wwwroot --no-daemon --spa'
            }
            if ([string]$artifact.startupCommand -cne $expectedStartupCommand) {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactContractInvalid:$($definition.kind):startupCommand")
            }
            $uri = Get-PM365TrustedRuntimeArtifactUri -Value ([string]$artifact.downloadUrl)
            $fileName = [string]$artifact.fileName
            if ([System.IO.Path]::GetFileName($fileName) -ne $fileName -or $fileName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*\.zip$') {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactContractInvalid:$($definition.kind):fileName")
            }

            $declaredHash = [string]$artifact.sha256
            if ($declaredHash -cnotmatch '^[0-9a-f]{64}$') {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactContractInvalid:$($definition.kind):sha256")
            }

            $archivePath = Join-Path $tempRoot $fileName
            try {
                Save-PM365RuntimeArtifactDownload -Uri $uri -OutputPath $archivePath -MaximumBytes 268435456
            } catch {
                throw [System.IO.IOException]::new("RuntimeArtifactDownloadFailed:$($definition.kind)")
            }

            $file = Get-Item -LiteralPath $archivePath -ErrorAction Stop
            if ($file.Length -le 0 -or $file.Length -gt 268435456) {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactSizeInvalid:$($definition.kind)")
            }

            $computedHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($computedHash -cne $declaredHash) {
                throw [System.Security.Cryptography.CryptographicException]::new("RuntimeArtifactIntegrityFailed:$($definition.kind)")
            }

            Test-PM365RuntimeArtifactArchive `
                -ArchivePath $archivePath `
                -Kind $definition.kind `
                -ReleaseId ([string]$release.releaseId)

            try {
                Publish-AzWebApp `
                    -ResourceGroupName $resourceGroupName `
                    -Name $definition.targetAppName `
                    -ArchivePath $file.FullName `
                    -Force `
                    -ErrorAction Stop | Out-Null
            } catch {
                throw [System.InvalidOperationException]::new("RuntimeArtifactPublishFailed:$($definition.kind)")
            }

            $evidence.artifacts += [pscustomobject][ordered]@{
                kind = $definition.kind
                fileName = $fileName
                sourceHost = $uri.Host
                byteCount = $file.Length
                declaredSha256 = $declaredHash
                computedSha256 = $computedHash
                targetAppName = $definition.targetAppName
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
        [string] $Value
    )

    $uri = $null
    if (-not [uri]::TryCreate($Value, [System.UriKind]::Absolute, [ref]$uri)) {
        throw [System.IO.InvalidDataException]::new('RuntimeArtifactContractInvalid:runtime:downloadUrl')
    }

    $isLocal = $uri.Host -in @('localhost', '127.0.0.1', '::1')
    $allowedHost = $uri.Host -in @('downloads.pagemaker365.com', 'downloads-staging.pagemaker365.com')
    if ((-not $allowedHost -and -not $isLocal) -or
        ($uri.Scheme -ne 'https' -and -not ($isLocal -and $uri.Scheme -eq 'http')) -or
        (-not $isLocal -and -not $uri.IsDefaultPort) -or
        -not [string]::IsNullOrWhiteSpace($uri.UserInfo) -or
        -not [string]::IsNullOrWhiteSpace($uri.Query) -or
        -not [string]::IsNullOrWhiteSpace($uri.Fragment)) {
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
        [string] $ReleaseId
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        if ($archive.Entries.Count -eq 0 -or $archive.Entries.Count -gt 100000) {
            throw [System.IO.InvalidDataException]::new("RuntimeArtifactArchiveInvalid:$Kind")
        }

        $totalLength = [long]0
        foreach ($entry in $archive.Entries) {
            $normalizedName = $entry.FullName.Replace('\', '/')
            if ($normalizedName.StartsWith('/') -or
                $normalizedName -match '(^|/)\.\.(/|$)' -or
                [System.IO.Path]::IsPathRooted($entry.FullName)) {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactArchiveInvalid:$Kind")
            }

            $totalLength += $entry.Length
            if ($totalLength -gt 1073741824) {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactArchiveInvalid:$Kind")
            }
        }

        $requiredEntries = if ($Kind -eq 'api') { @('dist/index.js', 'package.json') } else { @('index.html') }
        foreach ($requiredEntry in $requiredEntries) {
            if (-not ($archive.Entries.FullName.Replace('\', '/') -ccontains $requiredEntry)) {
                throw [System.IO.InvalidDataException]::new("RuntimeArtifactArchiveInvalid:$Kind")
            }
        }

        if ($Kind -eq 'portal') {
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
            $escapedReleaseId = [regex]::Escape($ReleaseId)
            $hasProduct = $indexContent -match '(?i)PageMaker365'
            $hasRelease = $indexContent -match "(?is)<meta\s+[^>]*name=[`"']pm365-release-id[`"'][^>]*content=[`"']$escapedReleaseId[`"']" -or
                $indexContent -match "(?is)<meta\s+[^>]*content=[`"']$escapedReleaseId[`"'][^>]*name=[`"']pm365-release-id[`"']"
            if (-not $hasProduct -or -not $hasRelease) {
                throw [System.IO.InvalidDataException]::new('RuntimeArtifactArchiveInvalid:portal')
            }
        }

        $true
    } finally {
        $archive.Dispose()
    }
}
