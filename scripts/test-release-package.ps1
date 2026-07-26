[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackagePath,

    [Parameter(Mandatory)]
    [string] $ArchivePath,

    [Parameter(Mandatory)]
    [string] $ExpectedVersion,

    [switch] $RequireSignature
)

$ErrorActionPreference = 'Stop'
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$ArchivePath = (Resolve-Path -LiteralPath $ArchivePath).Path
$archiveChecksumPath = "$ArchivePath.sha256"

if (-not (Test-Path -LiteralPath $archiveChecksumPath -PathType Leaf)) {
    throw "Archive checksum is missing: $archiveChecksumPath"
}

$verifierPath = Join-Path $PackagePath 'Verify-PageMaker365Installer.ps1'
$verifyArguments = @{ PackagePath = $PackagePath }
if (-not $RequireSignature) {
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
        [string] $Scenario
    )

    $failed = $false
    try {
        & $Action | Out-Null
    }
    catch {
        $failed = $true
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
}

$tamperPath = Join-Path $PackagePath 'RELEASE-NOTES.md'
$originalTamperBytes = [System.IO.File]::ReadAllBytes($tamperPath)
try {
    Add-Content -LiteralPath $tamperPath -Value 'tampered'
    Assert-VerificationFails `
        -Scenario 'modified manifest-listed file' `
        -Action { & $verifierPath -PackagePath $PackagePath -AllowUnsignedDevelopment }
}
finally {
    [System.IO.File]::WriteAllBytes($tamperPath, $originalTamperBytes)
}

$unexpectedPath = Join-Path $PackagePath 'unexpected-release-file.txt'
try {
    Set-Content -LiteralPath $unexpectedPath -Value 'unexpected' -Encoding ascii
    Assert-VerificationFails `
        -Scenario 'unexpected extracted file' `
        -Action { & $verifierPath -PackagePath $PackagePath -AllowUnsignedDevelopment }
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

Write-Host "Release package checks passed for PageMaker365 Installer $ExpectedVersion."
