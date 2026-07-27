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

function New-TestArchive {
    param(
        [string] $Kind,
        [string] $Path,
        [string] $ReleaseId = 'pm365-runtime-1.0.0+test'
    )

    $source = Join-Path $tempRoot "$Kind-source-$([guid]::NewGuid().ToString('N'))"
    New-Item -Path $source -ItemType Directory -Force | Out-Null
    if ($Kind -eq 'api') {
        $dist = Join-Path $source 'dist'
        New-Item -Path $dist -ItemType Directory -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $dist 'index.js') -Value 'console.log("api");' -Encoding utf8
        Set-Content -LiteralPath (Join-Path $source 'package.json') -Value '{"scripts":{"start":"node dist/index.js"}}' -Encoding utf8
    } else {
        $content = "<html><head><title>PageMaker365</title><meta name=`"pm365-release-id`" content=`"$ReleaseId`"></head></html>"
        Set-Content -LiteralPath (Join-Path $source 'index.html') -Value $content -Encoding utf8
    }

    Compress-Archive -Path (Join-Path $source '*') -DestinationPath $Path -Force
}

function New-UnsafeApiArchive {
    param(
        [string] $Path,
        [ValidateSet('duplicate', 'symlink')]
        [string] $UnsafeEntry
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $fileStream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create)
    $archive = [System.IO.Compression.ZipArchive]::new(
        $fileStream,
        [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entryName in @('dist/index.js', 'package.json')) {
            $entry = $archive.CreateEntry($entryName)
            $writer = [System.IO.StreamWriter]::new($entry.Open())
            try {
                $writer.Write('PageMaker365 runtime test')
            } finally {
                $writer.Dispose()
            }
        }

        if ($UnsafeEntry -eq 'duplicate') {
            $archive.CreateEntry('dist/index.js').Open().Dispose()
        } else {
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
        'https://downloads.pagemaker365.com/runtime/api.zip' = $apiArchive
        'https://downloads.pagemaker365.com/runtime/portal.zip' = $portalArchive
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
            api = [pscustomobject]@{
                fileName = 'pagemaker365-api.zip'
                downloadUrl = 'https://downloads.pagemaker365.com/runtime/api.zip'
                sha256 = (Get-FileHash -LiteralPath $apiArchive -Algorithm SHA256).Hash.ToLowerInvariant()
                startupCommand = 'node dist/index.js'
            }
            portal = [pscustomobject]@{
                fileName = 'pagemaker365-portal.zip'
                downloadUrl = 'https://downloads.pagemaker365.com/runtime/portal.zip'
                sha256 = (Get-FileHash -LiteralPath $portalArchive -Algorithm SHA256).Hash.ToLowerInvariant()
                startupCommand = 'pm2 serve /home/site/wwwroot --no-daemon --spa'
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
    $config.runtimeArtifacts.api.sha256 = '0' * 64
    $tampered = Publish-PM365RuntimeArtifacts -Config $config
    Assert-Equal 'Failed' $tampered.status 'A hash mismatch was accepted.'
    Assert-Equal 'RuntimeArtifactIntegrityFailed' $tampered.error.code 'A hash mismatch returned the wrong error code.'
    Assert-Equal 0 $script:publishCalls.Count 'A hash mismatch reached Azure publish.'
    $config.runtimeArtifacts.api.sha256 = $originalHash

    $config.runtimeArtifacts.api.downloadUrl = 'https://example.com/api.zip'
    $untrusted = Publish-PM365RuntimeArtifacts -Config $config
    Assert-Equal 'RuntimeArtifactContractInvalid' $untrusted.error.code 'An untrusted artifact URL returned the wrong error.'
    $config.runtimeArtifacts.api.downloadUrl = 'https://downloads.pagemaker365.com/runtime/api.zip'

    foreach ($unsafeEntry in @('duplicate', 'symlink')) {
        $unsafeArchive = Join-Path $tempRoot "api-$unsafeEntry.zip"
        New-UnsafeApiArchive -Path $unsafeArchive -UnsafeEntry $unsafeEntry
        $script:downloadMap['https://downloads.pagemaker365.com/runtime/api.zip'] = $unsafeArchive
        $config.runtimeArtifacts.api.sha256 = (Get-FileHash -LiteralPath $unsafeArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        $script:publishCalls = @()
        $unsafe = Publish-PM365RuntimeArtifacts -Config $config
        Assert-Equal 'RuntimeArtifactArchiveInvalid' $unsafe.error.code "An API archive with a $unsafeEntry entry returned the wrong error."
        Assert-Equal 0 $script:publishCalls.Count "An API archive with a $unsafeEntry entry reached Azure publish."
    }
    $script:downloadMap['https://downloads.pagemaker365.com/runtime/api.zip'] = $apiArchive
    $config.runtimeArtifacts.api.sha256 = $originalHash

    $stalePortalArchive = Join-Path $tempRoot 'portal-stale.zip'
    New-TestArchive -Kind portal -Path $stalePortalArchive -ReleaseId 'pm365-runtime-0.9.0+stale'
    $script:downloadMap['https://downloads.pagemaker365.com/runtime/portal.zip'] = $stalePortalArchive
    $config.runtimeArtifacts.portal.sha256 = (Get-FileHash -LiteralPath $stalePortalArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $stalePortal = Publish-PM365RuntimeArtifacts -Config $config
    Assert-Equal 'RuntimeArtifactArchiveInvalid' $stalePortal.error.code 'A stale portal release marker was accepted.'
    Assert-Equal 'portal' $stalePortal.error.artifactKind 'The stale portal error did not identify the portal artifact.'

    Write-Host 'Runtime artifact contract tests passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
