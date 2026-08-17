[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $Runtime = 'win-x64',

    [string] $OutputPath = '',

    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$')]
    [string] $Version = '0.1.0-dev',

    [string] $ReleaseNotesPath = '',

    [switch] $RequireCleanSource,

    [switch] $PrepareForArtifactSigning
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = 'C:\Program Files\dotnet'
if (Test-Path -LiteralPath $dotnetPath) {
    $env:Path = "$dotnetPath;$env:Path"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot 'artifacts\installer-package'
}

if ([string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
    $ReleaseNotesPath = Join-Path $repoRoot 'docs\release\RELEASE-NOTES-template.md'
}

$resolvedRepoRoot = (Resolve-Path -LiteralPath $repoRoot).Path.TrimEnd('\')
$OutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$resolvedOutputParent = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $resolvedOutputParent)) {
    New-Item -ItemType Directory -Path $resolvedOutputParent | Out-Null
}

$resolvedOutputParent = (Resolve-Path -LiteralPath $resolvedOutputParent).Path
$repoPrefix = "$resolvedRepoRoot\"
if (-not $resolvedOutputParent.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Package output must stay inside the repository. Requested path: $OutputPath"
}

if (-not (Test-Path -LiteralPath $ReleaseNotesPath -PathType Leaf)) {
    throw "Release notes template was not found: $ReleaseNotesPath"
}

Push-Location $repoRoot
try {
    $sourceCommit = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to resolve the source commit.'
    }

    $sourceCommittedAt = (git show -s --format=%cI HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to resolve the source commit timestamp.'
    }

    $sourceDirty = @((git status --porcelain)).Count -gt 0
}
finally {
    Pop-Location
}

if ($RequireCleanSource -and $sourceDirty) {
    throw 'Customer release packaging requires a clean Git worktree.'
}

$archivePath = "$OutputPath.zip"
$archiveChecksumPath = "$archivePath.sha256"
foreach ($path in @($OutputPath, $archivePath, $archiveChecksumPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $OutputPath | Out-Null

$publishPath = Join-Path $OutputPath 'app'
$appProject = Join-Path $repoRoot 'src\PageMaker365.Installer.App\PageMaker365.Installer.App.csproj'
dotnet publish $appProject `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained false `
    --output $publishPath `
    -p:Version=$Version `
    -p:InformationalVersion=$Version `
    -p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Get-ChildItem -LiteralPath $publishPath -Recurse -File -Filter *.pdb |
    Remove-Item -Force

@('modules', 'infra', 'rules', 'ai', 'samples', 'schemas') | ForEach-Object {
    Copy-Item `
        -LiteralPath (Join-Path $repoRoot $_) `
        -Destination (Join-Path $OutputPath $_) `
        -Recurse `
        -Force
}

$packageDocs = @(
    'assistant-api-contract.md',
    'deployment-contract.md',
    'onboarding-discovery-contract.md',
    'portal-install-package-handoff.md',
    'removal-evidence-callback-contract.md',
    'removal-policy.md',
    'runtime-artifact-contract.md',
    'runtime-secret-contract.md',
    'using-the-installer.md'
)
$packageDocsPath = Join-Path $OutputPath 'docs'
New-Item -ItemType Directory -Path $packageDocsPath -Force | Out-Null
$packageDocs | ForEach-Object {
    $sourcePath = Join-Path $repoRoot "docs\$_"
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Required package documentation is missing: $sourcePath"
    }

    Copy-Item -LiteralPath $sourcePath -Destination $packageDocsPath -Force
}
Copy-Item `
    -LiteralPath (Join-Path $repoRoot 'docs\customer\installer-distribution-verification.md') `
    -Destination $packageDocsPath `
    -Force

Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $OutputPath -Force
Copy-Item `
    -LiteralPath (Join-Path $repoRoot 'distribution\Verify-PageMaker365Installer.ps1') `
    -Destination $OutputPath `
    -Force
Copy-Item `
    -LiteralPath (Join-Path $repoRoot 'distribution\ReleaseSignatureValidation.ps1') `
    -Destination $OutputPath `
    -Force

$releaseNotes = (Get-Content -LiteralPath $ReleaseNotesPath -Raw).
    Replace('{{VERSION}}', $Version).
    Replace('{{SOURCE_COMMIT}}', $sourceCommit)
Set-Content `
    -LiteralPath (Join-Path $OutputPath 'RELEASE-NOTES.md') `
    -Value $releaseNotes `
    -Encoding utf8NoBOM

if ($PrepareForArtifactSigning) {
    [pscustomobject]@{
        outputPath = $OutputPath
        appPath = $publishPath
        version = $Version
        sourceCommit = $sourceCommit
        sourceCommittedAt = $sourceCommittedAt
        sourceDirty = $sourceDirty
        preparedForArtifactSigning = $true
    }
    return
}

 $manifestPath = Join-Path $OutputPath 'release-manifest.json'
 $checksumPath = Join-Path $OutputPath 'SHA256SUMS.txt'
    $manifestFiles = @(
        Get-ChildItem -LiteralPath $OutputPath -Recurse -File |
            Sort-Object FullName |
            ForEach-Object {
                $relativePath = [System.IO.Path]::GetRelativePath($OutputPath, $_.FullName).Replace('\', '/')
                $signatureRequired =
                    $relativePath -eq 'app/PageMaker365.Installer.exe' -or
                    $relativePath -like 'app/PageMaker365.Installer*.dll' -or
                    $_.Extension -in @('.ps1', '.psm1', '.psd1')
                [ordered]@{
                    path = $relativePath
                    length = $_.Length
                    sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                    signatureRequired = $signatureRequired
                }
            }
    )

    $manifest = [ordered]@{
        schemaVersion = '1.0'
        product = 'PageMaker365 Installer'
        version = $Version
        runtime = $Runtime
        configuration = $Configuration
        distributionFormat = 'zip'
        entryPoint = 'app/PageMaker365.Installer.exe'
        source = [ordered]@{
            repository = 'https://github.com/cloudbossdev/pagemaker365-installer'
            commit = $sourceCommit
            committedAt = $sourceCommittedAt
            dirty = $sourceDirty
        }
        signing = [ordered]@{
            status = 'UnsignedDevelopment'
            requiredForCustomerRelease = $true
            publisher = $null
            certificateThumbprint = $null
            timestampServer = $null
        }
        files = $manifestFiles
    }
    $manifest | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

    $checksumFiles = @(
        Get-ChildItem -LiteralPath $OutputPath -Recurse -File |
            Where-Object FullName -NE $checksumPath |
            Sort-Object FullName
    )
    $checksumLines = @(
        foreach ($file in $checksumFiles) {
            $relativePath = [System.IO.Path]::GetRelativePath($OutputPath, $file.FullName).Replace('\', '/')
            '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relativePath
        }
    )
    Set-Content -LiteralPath $checksumPath -Value $checksumLines -Encoding ascii

    Add-Type -AssemblyName System.IO.Compression
    $archive = [System.IO.Compression.ZipFile]::Open($archivePath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem -LiteralPath $OutputPath -Recurse -File |
            Sort-Object FullName |
            ForEach-Object {
                $entryName = [System.IO.Path]::GetRelativePath($OutputPath, $_.FullName).Replace('\', '/')
                $entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $input = [System.IO.File]::OpenRead($_.FullName)
                $output = $entry.Open()
                try {
                    $input.CopyTo($output)
                }
                finally {
                    $output.Dispose()
                    $input.Dispose()
                }
            }
    }
    finally {
        $archive.Dispose()
    }

    $archiveSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content `
        -LiteralPath $archiveChecksumPath `
        -Value "$archiveSha256  $([System.IO.Path]::GetFileName($archivePath))" `
        -Encoding ascii

 [pscustomobject]@{
     outputPath = $OutputPath
     appPath = $publishPath
     archivePath = $archivePath
     archiveChecksumPath = $archiveChecksumPath
     archiveSha256 = $archiveSha256
     manifestPath = $manifestPath
     manifestSignaturePath = $null
     version = $Version
     signed = $false
     sourceDirty = $sourceDirty
 }
