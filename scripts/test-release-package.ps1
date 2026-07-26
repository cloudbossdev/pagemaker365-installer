[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackagePath,

    [Parameter(Mandatory)]
    [string] $ArchivePath,

    [Parameter(Mandatory)]
    [string] $ExpectedVersion,

    [string] $ExpectedPublisher = '',

    [string] $ExpectedCertificateThumbprint = '',

    [switch] $RequireSignature
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$ArchivePath = (Resolve-Path -LiteralPath $ArchivePath).Path
$archiveChecksumPath = "$ArchivePath.sha256"

if (-not (Test-Path -LiteralPath $archiveChecksumPath -PathType Leaf)) {
    throw "Archive checksum is missing: $archiveChecksumPath"
}

$verifierPath = Join-Path $PackagePath 'Verify-PageMaker365Installer.ps1'
$verifyArguments = @{ PackagePath = $PackagePath }
if ($RequireSignature) {
    if ([string]::IsNullOrWhiteSpace($ExpectedPublisher) -or
        [string]::IsNullOrWhiteSpace($ExpectedCertificateThumbprint)) {
        throw 'Signed release tests require ExpectedPublisher and ExpectedCertificateThumbprint.'
    }
    $verifyArguments.ExpectedPublisher = $ExpectedPublisher
    $verifyArguments.ExpectedCertificateThumbprint = $ExpectedCertificateThumbprint
}
else {
    $verifyArguments.AllowUnsignedDevelopment = $true
}
$verification = & $verifierPath @verifyArguments
if ($verification.result -ne 'Verified') {
    throw 'The package verifier did not return a verified result.'
}

function Assert-VerificationFails {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Action,

        [Parameter(Mandatory)]
        [string] $Scenario,

        [string] $ExpectedMessagePattern = ''
    )

    $failed = $false
    try {
        & $Action | Out-Null
    }
    catch {
        $failed = $true
        if (-not [string]::IsNullOrWhiteSpace($ExpectedMessagePattern) -and
            $_.Exception.Message -notmatch $ExpectedMessagePattern) {
            throw "Release verification failed for '$Scenario', but not for the expected reason. $($_.Exception.Message)"
        }
    }

    if (-not $failed) {
        throw "Release verification did not fail for scenario: $Scenario"
    }
}

$manifest = Get-Content -LiteralPath (Join-Path $PackagePath 'release-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.version -ne $ExpectedVersion) {
    throw "Manifest version '$($manifest.version)' does not match '$ExpectedVersion'."
}
if ($RequireSignature -and $manifest.signing.status -ne 'Signed') {
    throw 'A customer release must report a Signed manifest state.'
}
if (-not $RequireSignature -and $manifest.signing.status -notin @('Signed', 'UnsignedDevelopment')) {
    throw "Unexpected signing state: $($manifest.signing.status)"
}

if ($manifest.signing.status -eq 'UnsignedDevelopment') {
    Assert-VerificationFails `
        -Scenario 'unsigned development package in customer mode' `
        -Action { & $verifierPath -PackagePath $PackagePath }

    $manifestPath = Join-Path $PackagePath 'release-manifest.json'
    $originalManifestBytes = [System.IO.File]::ReadAllBytes($manifestPath)
    try {
        $manifest.signing.status = 'Signed'
        $manifest | ConvertTo-Json -Depth 8 |
            Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
        Assert-VerificationFails `
            -Scenario 'self-declared signed package without an external signer identity' `
            -ExpectedMessagePattern 'expected publisher and certificate thumbprint' `
            -Action { & $verifierPath -PackagePath $PackagePath -AllowUnsignedDevelopment }
    }
    finally {
        [System.IO.File]::WriteAllBytes($manifestPath, $originalManifestBytes)
    }
}

$tamperPath = Join-Path $PackagePath 'RELEASE-NOTES.md'
$originalTamperBytes = [System.IO.File]::ReadAllBytes($tamperPath)
try {
    Add-Content -LiteralPath $tamperPath -Value 'tampered'
    Assert-VerificationFails `
        -Scenario 'modified manifest-listed file' `
        -Action { & $verifierPath @verifyArguments }
}
finally {
    [System.IO.File]::WriteAllBytes($tamperPath, $originalTamperBytes)
}

$unexpectedPath = Join-Path $PackagePath 'unexpected-release-file.txt'
try {
    Set-Content -LiteralPath $unexpectedPath -Value 'unexpected' -Encoding ascii
    Assert-VerificationFails `
        -Scenario 'unexpected extracted file' `
        -Action { & $verifierPath @verifyArguments }
}
finally {
    Remove-Item -LiteralPath $unexpectedPath -Force -ErrorAction SilentlyContinue
}

$exePath = Join-Path $PackagePath 'app\PageMaker365.Installer.exe'
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
if ($versionInfo.ProductName -ne 'PageMaker365 Installer' -or
    $versionInfo.CompanyName -ne 'PageMaker365' -or
    $versionInfo.FileDescription -ne 'PageMaker365 Installer' -or
    $versionInfo.Comments -ne 'Guided Azure and SharePoint deployment for PageMaker365' -or
    $versionInfo.ProductVersion -ne $ExpectedVersion) {
    throw "Executable metadata is incomplete or incorrect: $($versionInfo | Format-List | Out-String)"
}

Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::ExtractAssociatedIcon($exePath)
if ($null -eq $icon -or $icon.Width -lt 16 -or $icon.Height -lt 16) {
    throw 'The installer executable does not expose a usable Windows icon.'
}
$icon.Dispose()

$checksumLine = Get-Content -LiteralPath $archiveChecksumPath -Raw
if ($checksumLine -notmatch '^([0-9a-f]{64})  (.+\.zip)\s*$') {
    throw 'The archive checksum sidecar has an invalid format.'
}
$actualArchiveHash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($Matches[1] -ne $actualArchiveHash -or $Matches[2] -ne [System.IO.Path]::GetFileName($ArchivePath)) {
    throw 'The ZIP archive checksum does not match its sidecar.'
}

Add-Type -AssemblyName System.IO.Compression
$archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
try {
    $archivePaths = @($archive.Entries | ForEach-Object FullName | Sort-Object)
    foreach ($path in $archivePaths) {
        if ([System.IO.Path]::IsPathRooted($path) -or
            $path.Contains('\') -or
            $path.Contains(':') -or
            $path.Split('/') -contains '..') {
            throw "The ZIP contains an unsafe path: $path"
        }
    }

    $packagePaths = @(
        Get-ChildItem -LiteralPath $PackagePath -Recurse -File |
            ForEach-Object { [System.IO.Path]::GetRelativePath($PackagePath, $_.FullName).Replace('\', '/') } |
            Sort-Object
    )
    if (($archivePaths -join "`n") -ne ($packagePaths -join "`n")) {
        throw 'The ZIP inventory does not match the verified package directory.'
    }
}
finally {
    $archive.Dispose()
}

$signedWorkflowPath = Join-Path $repoRoot '.github\workflows\signed-release-candidate.yml'
$signedWorkflow = Get-Content -LiteralPath $signedWorkflowPath -Raw
foreach ($requiredPolicy in @(
    "environment: production-signing",
    "`$env:GITHUB_REF -ne 'refs/heads/main'",
    'PM365_CODESIGN_THUMBPRINT: ${{ vars.PM365_CODESIGN_THUMBPRINT }}',
    '-ExpectedCertificateThumbprint $env:PM365_CODESIGN_THUMBPRINT'
)) {
    if (-not $signedWorkflow.Contains($requiredPolicy, [StringComparison]::Ordinal)) {
        throw "Signed release workflow policy is missing: $requiredPolicy"
    }
}

$verifyStepIndex = $signedWorkflow.IndexOf('- name: Verify repository', [StringComparison]::Ordinal)
$certificateStepIndex = $signedWorkflow.IndexOf('- name: Materialize signing certificate', [StringComparison]::Ordinal)
if ($verifyStepIndex -lt 0 -or $certificateStepIndex -lt 0 -or $verifyStepIndex -gt $certificateStepIndex) {
    throw 'Repository verification must complete before the signing certificate is materialized.'
}

Write-Host "Release package checks passed for PageMaker365 Installer $ExpectedVersion."
