[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$moduleRoot = Join-Path $repoRoot 'modules\PageMaker365.Install'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "pm365-runtime-artifact-tests\$([guid]::NewGuid().ToString('N'))"

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param([object] $Expected, [object] $Actual, [string] $Message)
    if ($Expected -ne $Actual) { throw "$Message Expected '$Expected', actual '$Actual'." }
}

function New-TestProvenance {
    param(
        [string] $Kind,
        [string] $ReleaseId = 'pm365-runtime-1.0.0+test',
        [string] $RuntimeVersion = '1.0.0',
        [hashtable] $Overrides = @{},
        [string[]] $Remove = @(),
        [hashtable] $Rename = @{},
        [hashtable] $Additional = @{}
    )

    $values = [ordered]@{
        schemaVersion = 'pagemaker365.runtime-provenance.v1'
        product = 'PageMaker365'
        artifactKind = $Kind
        releaseId = $ReleaseId
        runtimeVersion = $RuntimeVersion
        sourceRepository = 'cloudbossdev/spo-ui'
        sourceCommit = 'a' * 40
        dependencyLockSha256 = 'b' * 64
        startupCommand = if ($Kind -eq 'api') { 'node dist/index.js' } else { 'node .pm365/start-portal-runtime.mjs' }
    }
    foreach ($name in $Overrides.Keys) { $values[$name] = $Overrides[$name] }
    foreach ($name in $Remove) { $values.Remove($name) }
    foreach ($name in $Rename.Keys) {
        $value = $values[$name]
        $values.Remove($name)
        $values[$Rename[$name]] = $value
    }
    foreach ($name in $Additional.Keys) { $values[$name] = $Additional[$name] }
    [pscustomobject]$values
}

function New-TestArchive {
    param(
        [string] $Kind,
        [string] $Path,
        [string] $ReleaseId = 'pm365-runtime-1.0.0+test',
        [string] $RuntimeVersion = '1.0.0',
        [string] $MarkerReleaseId = '',
        [object] $Provenance
    )

    $source = Join-Path $tempRoot "$Kind-source-$([guid]::NewGuid().ToString('N'))"
    New-Item -Path $source -ItemType Directory -Force | Out-Null
    $metadataDirectory = Join-Path $source '.pm365'
    New-Item -Path $metadataDirectory -ItemType Directory -Force | Out-Null
    if ($null -eq $Provenance) {
        $Provenance = New-TestProvenance -Kind $Kind -ReleaseId $ReleaseId -RuntimeVersion $RuntimeVersion
    }
    $Provenance | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $metadataDirectory 'provenance.json') -Encoding utf8
    if ($Kind -eq 'api') {
        $dist = Join-Path $source 'dist'
        New-Item -Path $dist -ItemType Directory -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $dist 'index.js') -Value 'console.log("api");' -Encoding utf8
        Set-Content -LiteralPath (Join-Path $source 'package.json') -Value '{"scripts":{"start":"node dist/index.js"}}' -Encoding utf8
    } else {
        if ([string]::IsNullOrEmpty($MarkerReleaseId)) {
            $MarkerReleaseId = $ReleaseId
        }
        $content = "<html><head><title>PageMaker365</title><meta name=`"pm365-release-id`" content=`"$MarkerReleaseId`"></head></html>"
        Set-Content -LiteralPath (Join-Path $source 'index.html') -Value $content -Encoding utf8
        Set-Content -LiteralPath (Join-Path $source 'auth-redirect.html') -Value '<!doctype html><title>Authentication redirect</title>' -Encoding utf8
        Set-Content -LiteralPath (Join-Path $metadataDirectory 'start-portal-runtime.mjs') -Value 'console.log("launcher");' -Encoding utf8
        Set-Content -LiteralPath (Join-Path $metadataDirectory 'generate-web-runtime-config.mjs') -Value 'export function generate() {}' -Encoding utf8
    }

    Compress-Archive -Path (Join-Path $source '*') -DestinationPath $Path -Force
}

function New-UnsafeApiArchive {
    param(
        [string] $Path,
        [ValidateSet('duplicate', 'symlink', 'backslash', 'empty', 'dot')]
        [string] $UnsafeEntry
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $fileStream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create)
    $archive = [System.IO.Compression.ZipArchive]::new(
        $fileStream,
        [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $entryNames = switch ($UnsafeEntry) {
            'backslash' { @('dist\index.js', 'package.json', '.pm365/provenance.json') }
            'empty' { @('dist//index.js', 'package.json', '.pm365/provenance.json') }
            'dot' { @('dist/./index.js', 'package.json', '.pm365/provenance.json') }
            default { @('dist/index.js', 'package.json', '.pm365/provenance.json') }
        }
        foreach ($entryName in $entryNames) {
            $entry = $archive.CreateEntry($entryName)
            $writer = [System.IO.StreamWriter]::new($entry.Open())
            try {
                $content = if ($entryName -eq '.pm365/provenance.json') {
                    New-TestProvenance -Kind api | ConvertTo-Json
                } else {
                    'PageMaker365 runtime test'
                }
                $writer.Write($content)
            } finally {
                $writer.Dispose()
            }
        }

        if ($UnsafeEntry -eq 'duplicate') {
            $archive.CreateEntry('dist/index.js').Open().Dispose()
        } elseif ($UnsafeEntry -eq 'symlink') {
            $link = $archive.CreateEntry('dist/runtime-link')
            $unixLinkAttributes = [byte[]](0x00, 0x00, 0xFF, 0xA1)
            $link.ExternalAttributes = [System.BitConverter]::ToInt32(
                $unixLinkAttributes,
                0)
            $link.Open().Dispose()
        }
    } finally {
        $archive.Dispose()
        $fileStream.Dispose()
    }
}

New-Item -Path $tempRoot -ItemType Directory -Force | Out-Null

try {
    Get-ChildItem -Path (Join-Path $moduleRoot 'Private') -Filter '*.ps1' -File |
        ForEach-Object { . $_.FullName }
    Get-ChildItem -Path (Join-Path $moduleRoot 'Public') -Filter '*.ps1' -File |
        ForEach-Object { . $_.FullName }

    $apiArchive = Join-Path $tempRoot 'api.zip'
    $portalArchive = Join-Path $tempRoot 'portal.zip'
    New-TestArchive -Kind api -Path $apiArchive
    New-TestArchive -Kind portal -Path $portalArchive

    $script:downloadMap = @{
        'https://downloads.pagemaker365.com/runtime/pagemaker365-api.zip' = $apiArchive
        'https://downloads.pagemaker365.com/runtime/pagemaker365-portal.zip' = $portalArchive
    }
    $script:publishCalls = @()

    function Save-PM365RuntimeArtifactDownload {
        param(
            [uri] $Uri,
            [string] $OutputPath,
            [long] $MaximumBytes
        )

        Copy-Item -LiteralPath $script:downloadMap[$Uri.AbsoluteUri] -Destination $OutputPath -Force
    }

    function Publish-AzWebApp {
        param(
            [string] $ResourceGroupName,
            [string] $Name,
            [string] $ArchivePath,
            [switch] $Force,
            [System.Management.Automation.ActionPreference] $ErrorAction
        )

        $script:publishCalls += [pscustomobject]@{
            resourceGroupName = $ResourceGroupName
            name = $Name
            archivePath = $ArchivePath
        }
    }

    $config = [pscustomobject]@{
        azure = [pscustomobject]@{
            resourceGroupName = 'rg-pm365-test'
            resourceNames = [pscustomobject]@{
                apiAppName = 'app-pm365-api-test'
                portalAppName = 'app-pm365-portal-test'
            }
        }
        runtimeArtifacts = [pscustomobject]@{
            releaseId = 'pm365-runtime-1.0.0+test'
            runtimeVersion = '1.0.0'
            sourceCommit = 'a' * 40
            api = [pscustomobject]@{
                fileName = 'pagemaker365-api.zip'
                sizeBytes = (Get-Item -LiteralPath $apiArchive).Length
                downloadUrl = 'https://downloads.pagemaker365.com/runtime/pagemaker365-api.zip'
                sha256 = (Get-FileHash -LiteralPath $apiArchive -Algorithm SHA256).Hash.ToLowerInvariant()
                startupCommand = 'node dist/index.js'
            }
            portal = [pscustomobject]@{
                fileName = 'pagemaker365-portal.zip'
                sizeBytes = (Get-Item -LiteralPath $portalArchive).Length
                downloadUrl = 'https://downloads.pagemaker365.com/runtime/pagemaker365-portal.zip'
                sha256 = (Get-FileHash -LiteralPath $portalArchive -Algorithm SHA256).Hash.ToLowerInvariant()
                startupCommand = 'node .pm365/start-portal-runtime.mjs'
            }
        }
    }

    $passed = Publish-PM365RuntimeArtifacts -Config $config
    Assert-Equal 'Passed' $passed.status 'Verified artifacts did not pass.'
    Assert-Equal 2 $passed.artifacts.Count 'Both runtime artifacts were not recorded.'
    Assert-Equal 2 $script:publishCalls.Count 'Both runtime artifacts were not published.'
    Assert-Equal 'app-pm365-api-test' $script:publishCalls[0].name 'API artifact targeted the wrong app.'
    Assert-Equal 'app-pm365-portal-test' $script:publishCalls[1].name 'Portal artifact targeted the wrong app.'
    Assert-True (($passed | ConvertTo-Json -Depth 8) -notmatch [regex]::Escape($tempRoot)) 'Runtime evidence exposed a temporary local path.'

    $script:publishCalls = @()
    $originalHash = $config.runtimeArtifacts.api.sha256
    $originalSize = $config.runtimeArtifacts.api.sizeBytes
    $config.runtimeArtifacts.api.sha256 = '0' * 64
    $tampered = Publish-PM365RuntimeArtifacts -Config $config
    Assert-Equal 'Failed' $tampered.status 'A hash mismatch was accepted.'
    Assert-Equal 'RuntimeArtifactIntegrityFailed' $tampered.error.code 'A hash mismatch returned the wrong error code.'
    Assert-Equal 0 $script:publishCalls.Count 'A hash mismatch reached Azure publish.'
    $config.runtimeArtifacts.api.sha256 = $originalHash

    $config.runtimeArtifacts.api.sizeBytes = $originalSize + 1
    $wrongSize = Publish-PM365RuntimeArtifacts -Config $config
    Assert-Equal 'RuntimeArtifactSizeInvalid' $wrongSize.error.code 'A declared size mismatch returned the wrong error code.'
    Assert-Equal 0 $script:publishCalls.Count 'A declared size mismatch reached Azure publish.'
    $config.runtimeArtifacts.api.sizeBytes = $originalSize

    foreach ($mismatch in @(
        @{ Name = 'schema'; Overrides = @{ schemaVersion = 'pagemaker365.runtime-provenance.v2' } },
        @{ Name = 'product'; Overrides = @{ product = 'pagemaker365' } },
        @{ Name = 'kind'; Overrides = @{ artifactKind = 'portal' } },
        @{ Name = 'release'; Overrides = @{ releaseId = 'pm365-runtime-1.0.0+other' } },
        @{ Name = 'version'; Overrides = @{ runtimeVersion = '1.0.1' } },
        @{ Name = 'repository'; Overrides = @{ sourceRepository = 'example/spo-ui' } },
        @{ Name = 'commit'; Overrides = @{ sourceCommit = 'c' * 40 } },
        @{ Name = 'lock'; Overrides = @{ dependencyLockSha256 = 'B' * 64 } },
        @{ Name = 'startup'; Overrides = @{ startupCommand = 'node other.js' } }
    )) {
        $mismatchArchive = Join-Path $tempRoot "api-provenance-$($mismatch.Name).zip"
        $provenance = New-TestProvenance -Kind api -Overrides $mismatch.Overrides
        New-TestArchive -Kind api -Path $mismatchArchive -Provenance $provenance
        $script:downloadMap['https://downloads.pagemaker365.com/runtime/pagemaker365-api.zip'] = $mismatchArchive
        $config.runtimeArtifacts.api.sha256 = (Get-FileHash -LiteralPath $mismatchArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        $config.runtimeArtifacts.api.sizeBytes = (Get-Item -LiteralPath $mismatchArchive).Length
        $script:publishCalls = @()
        $invalidProvenance = Publish-PM365RuntimeArtifacts -Config $config
        Assert-Equal 'RuntimeArtifactArchiveInvalid' $invalidProvenance.error.code "An API archive with mismatched provenance $($mismatch.Name) was accepted."
        Assert-Equal 0 $script:publishCalls.Count "An API archive with mismatched provenance $($mismatch.Name) reached Azure publish."
    }

    foreach ($shape in @(
        @{ Name = 'additional'; Provenance = (New-TestProvenance -Kind api -Additional @{ unexpected = 'value' }) },
        @{ Name = 'missing'; Provenance = (New-TestProvenance -Kind api -Remove @('dependencyLockSha256')) },
        @{ Name = 'wrong-case'; Provenance = (New-TestProvenance -Kind api -Rename @{ product = 'Product' }) }
    )) {
        $shapeArchive = Join-Path $tempRoot "api-provenance-$($shape.Name).zip"
        New-TestArchive -Kind api -Path $shapeArchive -Provenance $shape.Provenance
        $script:downloadMap['https://downloads.pagemaker365.com/runtime/pagemaker365-api.zip'] = $shapeArchive
        $config.runtimeArtifacts.api.sha256 = (Get-FileHash -LiteralPath $shapeArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        $config.runtimeArtifacts.api.sizeBytes = (Get-Item -LiteralPath $shapeArchive).Length
        $script:publishCalls = @()
        $invalidShape = Publish-PM365RuntimeArtifacts -Config $config
        Assert-Equal 'RuntimeArtifactArchiveInvalid' $invalidShape.error.code "An API archive with $($shape.Name) provenance fields was accepted."
        Assert-Equal 0 $script:publishCalls.Count "An API archive with $($shape.Name) provenance fields reached Azure publish."
    }

    foreach ($propertyName in @(
        'schemaVersion',
        'product',
        'artifactKind',
        'releaseId',
        'runtimeVersion',
        'sourceRepository',
        'sourceCommit',
        'dependencyLockSha256',
        'startupCommand'
    )) {
        $typedArchive = Join-Path $tempRoot "api-provenance-non-string-$propertyName.zip"
        $typedProvenance = New-TestProvenance -Kind api -Overrides @{ $propertyName = 123 }
        New-TestArchive -Kind api -Path $typedArchive -Provenance $typedProvenance
        $script:downloadMap['https://downloads.pagemaker365.com/runtime/pagemaker365-api.zip'] = $typedArchive
        $config.runtimeArtifacts.api.sha256 = (Get-FileHash -LiteralPath $typedArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        $config.runtimeArtifacts.api.sizeBytes = (Get-Item -LiteralPath $typedArchive).Length
        $script:publishCalls = @()
        $invalidType = Publish-PM365RuntimeArtifacts -Config $config
        Assert-Equal 'RuntimeArtifactArchiveInvalid' $invalidType.error.code "A non-string provenance $propertyName was accepted."
        Assert-Equal 0 $script:publishCalls.Count "A non-string provenance $propertyName reached Azure publish."
    }

    $missingProvenanceArchive = Join-Path $tempRoot 'api-missing-provenance.zip'
    New-TestArchive -Kind api -Path $missingProvenanceArchive
    $missingProvenanceZip = [System.IO.Compression.ZipFile]::Open($missingProvenanceArchive, [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        ($missingProvenanceZip.Entries | Where-Object FullName -CEQ '.pm365/provenance.json').Delete()
    } finally {
        $missingProvenanceZip.Dispose()
    }
    $script:downloadMap['https://downloads.pagemaker365.com/runtime/pagemaker365-api.zip'] = $missingProvenanceArchive
    $config.runtimeArtifacts.api.sha256 = (Get-FileHash -LiteralPath $missingProvenanceArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $config.runtimeArtifacts.api.sizeBytes = (Get-Item -LiteralPath $missingProvenanceArchive).Length
    $script:publishCalls = @()
    $missingProvenance = Publish-PM365RuntimeArtifacts -Config $config
    Assert-Equal 'RuntimeArtifactArchiveInvalid' $missingProvenance.error.code 'An API archive missing provenance was accepted.'
    Assert-Equal 0 $script:publishCalls.Count 'An API archive missing provenance reached Azure publish.'

    $script:downloadMap['https://downloads.pagemaker365.com/runtime/pagemaker365-api.zip'] = $apiArchive
    $config.runtimeArtifacts.api.sha256 = $originalHash
    $config.runtimeArtifacts.api.sizeBytes = $originalSize

    $config.runtimeArtifacts.api.downloadUrl = 'https://example.com/api.zip'
    $untrusted = Publish-PM365RuntimeArtifacts -Config $config
    Assert-Equal 'RuntimeArtifactContractInvalid' $untrusted.error.code 'An untrusted artifact URL returned the wrong error.'
    $config.runtimeArtifacts.api.downloadUrl = 'https://downloads.pagemaker365.com/runtime/pagemaker365-api.zip'

    $config.runtimeArtifacts.portal.downloadUrl = 'https://downloads.pagemaker365.com/runtime/other/pagemaker365-portal.zip'
    $script:publishCalls = @()
    $splitRelease = Publish-PM365RuntimeArtifacts -Config $config
    Assert-Equal 'RuntimeArtifactContractInvalid' $splitRelease.error.code 'Different API and portal release directories were accepted.'
    Assert-Equal 0 $script:publishCalls.Count 'A split runtime release directory reached Azure publish.'
    $config.runtimeArtifacts.portal.downloadUrl = 'https://downloads.pagemaker365.com/runtime/pagemaker365-portal.zip'

    foreach ($unsafeEntry in @('duplicate', 'symlink', 'backslash', 'empty', 'dot')) {
        $unsafeArchive = Join-Path $tempRoot "api-$unsafeEntry.zip"
        New-UnsafeApiArchive -Path $unsafeArchive -UnsafeEntry $unsafeEntry
        $script:downloadMap['https://downloads.pagemaker365.com/runtime/pagemaker365-api.zip'] = $unsafeArchive
        $config.runtimeArtifacts.api.sha256 = (Get-FileHash -LiteralPath $unsafeArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        $config.runtimeArtifacts.api.sizeBytes = (Get-Item -LiteralPath $unsafeArchive).Length
        $script:publishCalls = @()
        $unsafe = Publish-PM365RuntimeArtifacts -Config $config
        Assert-Equal 'RuntimeArtifactArchiveInvalid' $unsafe.error.code "An API archive with a $unsafeEntry entry returned the wrong error."
        Assert-Equal 0 $script:publishCalls.Count "An API archive with a $unsafeEntry entry reached Azure publish."
    }
    $script:downloadMap['https://downloads.pagemaker365.com/runtime/pagemaker365-api.zip'] = $apiArchive
    $config.runtimeArtifacts.api.sha256 = $originalHash
    $config.runtimeArtifacts.api.sizeBytes = $originalSize

    $stalePortalArchive = Join-Path $tempRoot 'portal-stale.zip'
    New-TestArchive -Kind portal -Path $stalePortalArchive -ReleaseId 'pm365-runtime-0.9.0+stale'
    $script:downloadMap['https://downloads.pagemaker365.com/runtime/pagemaker365-portal.zip'] = $stalePortalArchive
    $config.runtimeArtifacts.portal.sha256 = (Get-FileHash -LiteralPath $stalePortalArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $config.runtimeArtifacts.portal.sizeBytes = (Get-Item -LiteralPath $stalePortalArchive).Length
    $script:publishCalls = @()
    $stalePortal = Publish-PM365RuntimeArtifacts -Config $config
    Assert-Equal 'RuntimeArtifactArchiveInvalid' $stalePortal.error.code 'A stale portal release marker was accepted.'
    Assert-Equal 'portal' $stalePortal.error.artifactKind 'The stale portal error did not identify the portal artifact.'
    Assert-Equal 0 $script:publishCalls.Count 'A stale portal archive reached Azure publish.'

    $caseMismatchedPortalArchive = Join-Path $tempRoot 'portal-release-case-mismatch.zip'
    New-TestArchive `
        -Kind portal `
        -Path $caseMismatchedPortalArchive `
        -MarkerReleaseId 'PM365-runtime-1.0.0+test'
    $script:downloadMap['https://downloads.pagemaker365.com/runtime/pagemaker365-portal.zip'] = $caseMismatchedPortalArchive
    $config.runtimeArtifacts.portal.sha256 = (Get-FileHash -LiteralPath $caseMismatchedPortalArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $config.runtimeArtifacts.portal.sizeBytes = (Get-Item -LiteralPath $caseMismatchedPortalArchive).Length
    $script:publishCalls = @()
    $caseMismatchedPortal = Publish-PM365RuntimeArtifacts -Config $config
    Assert-Equal 'RuntimeArtifactArchiveInvalid' $caseMismatchedPortal.error.code 'A case-mismatched portal release marker was accepted.'
    Assert-Equal 0 $script:publishCalls.Count 'A case-mismatched portal release marker reached Azure publish.'

    $obsoletePortalArchive = Join-Path $tempRoot 'portal-obsolete-swa.zip'
    New-TestArchive -Kind portal -Path $obsoletePortalArchive
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $obsoleteArchive = [System.IO.Compression.ZipFile]::Open($obsoletePortalArchive, [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        $obsoleteArchive.CreateEntry('staticwebapp.config.json').Open().Dispose()
    } finally {
        $obsoleteArchive.Dispose()
    }
    $script:downloadMap['https://downloads.pagemaker365.com/runtime/pagemaker365-portal.zip'] = $obsoletePortalArchive
    $config.runtimeArtifacts.portal.sha256 = (Get-FileHash -LiteralPath $obsoletePortalArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $config.runtimeArtifacts.portal.sizeBytes = (Get-Item -LiteralPath $obsoletePortalArchive).Length
    $script:publishCalls = @()
    $obsoletePortal = Publish-PM365RuntimeArtifacts -Config $config
    Assert-Equal 'RuntimeArtifactArchiveInvalid' $obsoletePortal.error.code 'A portal archive containing obsolete Static Web Apps configuration was accepted.'
    Assert-Equal 0 $script:publishCalls.Count 'An invalid portal archive reached Azure publish.'

    $missingLauncherArchive = Join-Path $tempRoot 'portal-missing-launcher.zip'
    New-TestArchive -Kind portal -Path $missingLauncherArchive
    $missingArchive = [System.IO.Compression.ZipFile]::Open($missingLauncherArchive, [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        ($missingArchive.Entries | Where-Object FullName -CEQ '.pm365/generate-web-runtime-config.mjs').Delete()
    } finally {
        $missingArchive.Dispose()
    }
    $script:downloadMap['https://downloads.pagemaker365.com/runtime/pagemaker365-portal.zip'] = $missingLauncherArchive
    $config.runtimeArtifacts.portal.sha256 = (Get-FileHash -LiteralPath $missingLauncherArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $config.runtimeArtifacts.portal.sizeBytes = (Get-Item -LiteralPath $missingLauncherArchive).Length
    $script:publishCalls = @()
    $missingLauncher = Publish-PM365RuntimeArtifacts -Config $config
    Assert-Equal 'RuntimeArtifactArchiveInvalid' $missingLauncher.error.code 'A portal archive missing the governed runtime generator was accepted.'
    Assert-Equal 0 $script:publishCalls.Count 'A portal archive missing the runtime generator reached Azure publish.'

    foreach ($missingPortalEntry in @('auth-redirect.html', '.pm365/provenance.json')) {
        $safeEntryName = $missingPortalEntry.Replace('/', '-')
        $missingPortalEntryArchive = Join-Path $tempRoot "portal-missing-$safeEntryName.zip"
        New-TestArchive -Kind portal -Path $missingPortalEntryArchive
        $missingPortalEntryZip = [System.IO.Compression.ZipFile]::Open(
            $missingPortalEntryArchive,
            [System.IO.Compression.ZipArchiveMode]::Update)
        try {
            ($missingPortalEntryZip.Entries | Where-Object FullName -CEQ $missingPortalEntry).Delete()
        } finally {
            $missingPortalEntryZip.Dispose()
        }
        $script:downloadMap['https://downloads.pagemaker365.com/runtime/pagemaker365-portal.zip'] = $missingPortalEntryArchive
        $config.runtimeArtifacts.portal.sha256 = (Get-FileHash -LiteralPath $missingPortalEntryArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        $config.runtimeArtifacts.portal.sizeBytes = (Get-Item -LiteralPath $missingPortalEntryArchive).Length
        $script:publishCalls = @()
        $missingPortalEntryResult = Publish-PM365RuntimeArtifacts -Config $config
        Assert-Equal 'RuntimeArtifactArchiveInvalid' $missingPortalEntryResult.error.code "A portal archive missing $missingPortalEntry was accepted."
        Assert-Equal 0 $script:publishCalls.Count "A portal archive missing $missingPortalEntry reached Azure publish."
    }

    Write-Host 'Runtime artifact contract tests passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
