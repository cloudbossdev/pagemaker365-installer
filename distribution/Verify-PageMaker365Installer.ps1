[CmdletBinding()]
param(
    [string] $PackagePath = $PSScriptRoot,

    [string] $ExpectedPublisher = '',

    [string] $ExpectedCertificateThumbprint = '',

    [switch] $AllowUnsignedDevelopment
)

$ErrorActionPreference = 'Stop'
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$manifestPath = Join-Path $PackagePath 'release-manifest.json'
$checksumPath = Join-Path $PackagePath 'SHA256SUMS.txt'

foreach ($path in @($manifestPath, $checksumPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release evidence is missing: $path"
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne '1.0' -or $manifest.product -ne 'PageMaker365 Installer') {
    throw 'The release manifest is not a supported PageMaker365 Installer manifest.'
}

if ($manifest.signing.status -ne 'Signed' -and -not $AllowUnsignedDevelopment) {
    throw 'This package is not signed for customer release. Obtain a signed PageMaker365 distribution.'
}

$normalizedExpectedThumbprint = ([string]$ExpectedCertificateThumbprint).Replace(' ', '').ToUpperInvariant()
if ($manifest.signing.status -eq 'Signed') {
    if ([string]::IsNullOrWhiteSpace($ExpectedPublisher) -or
        [string]::IsNullOrWhiteSpace($normalizedExpectedThumbprint)) {
        throw 'Signed-package verification requires the expected publisher and certificate thumbprint from the official PageMaker365 release record.'
    }
    if ($normalizedExpectedThumbprint -notmatch '^[0-9A-F]{40}$') {
        throw 'ExpectedCertificateThumbprint must contain exactly 40 hexadecimal characters.'
    }
    if ($manifest.signing.publisher -ne $ExpectedPublisher -or
        ([string]$manifest.signing.certificateThumbprint).ToUpperInvariant() -ne $normalizedExpectedThumbprint) {
        throw 'The release manifest signer does not match the official PageMaker365 release identity.'
    }

    $verifierSignature = Get-AuthenticodeSignature -LiteralPath $PSCommandPath
    if ($verifierSignature.Status -ne 'Valid' -or
        $null -eq $verifierSignature.SignerCertificate -or
        $verifierSignature.SignerCertificate.Subject -ne $ExpectedPublisher -or
        $verifierSignature.SignerCertificate.Thumbprint.ToUpperInvariant() -ne $normalizedExpectedThumbprint) {
        throw 'The verification script is not signed by the official PageMaker365 release identity.'
    }
}

$expectedPaths = @($manifest.files | ForEach-Object { [string]$_.path })
$duplicatePaths = @($expectedPaths | Group-Object | Where-Object Count -gt 1)
if ($duplicatePaths.Count -gt 0) {
    throw "The release manifest contains duplicate paths: $($duplicatePaths.Name -join ', ')"
}

foreach ($file in $manifest.files) {
    $relativePath = [string]$file.path
    if ([string]::IsNullOrWhiteSpace($relativePath) -or
        [System.IO.Path]::IsPathRooted($relativePath) -or
        $relativePath.Contains('\') -or
        $relativePath.Contains(':') -or
        $relativePath.Split('/') -contains '..') {
        throw "The release manifest contains an unsafe path: $relativePath"
    }

    $fullPath = Join-Path $PackagePath $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "A release file is missing: $relativePath"
    }

    $actualHash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne [string]$file.sha256) {
        throw "SHA-256 verification failed for $relativePath."
    }

    if ((Get-Item -LiteralPath $fullPath).Length -ne [long]$file.length) {
        throw "Length verification failed for $relativePath."
    }

    if ($file.signatureRequired -and $manifest.signing.status -eq 'Signed') {
        $signature = Get-AuthenticodeSignature -LiteralPath $fullPath
        if ($signature.Status -ne 'Valid') {
            throw "Authenticode verification failed for ${relativePath}: $($signature.StatusMessage)"
        }

        if ($signature.SignerCertificate.Thumbprint -ne $manifest.signing.certificateThumbprint -or
            $signature.SignerCertificate.Subject -ne $manifest.signing.publisher) {
            throw "The signer does not match the release manifest for $relativePath."
        }

        if ($signature.SignerCertificate.Thumbprint.ToUpperInvariant() -ne $normalizedExpectedThumbprint -or
            $signature.SignerCertificate.Subject -ne $ExpectedPublisher) {
            throw "The signer does not match the official PageMaker365 release identity for $relativePath."
        }
    }
}

$actualPayloadPaths = @(
    Get-ChildItem -LiteralPath $PackagePath -Recurse -File |
        ForEach-Object { [System.IO.Path]::GetRelativePath($PackagePath, $_.FullName).Replace('\', '/') } |
        Where-Object { $_ -notin @('release-manifest.json', 'SHA256SUMS.txt') } |
        Sort-Object
)
if (($actualPayloadPaths -join "`n") -ne (($expectedPaths | Sort-Object) -join "`n")) {
    throw 'The extracted package inventory does not match the release manifest.'
}

$checksumEntries = @{}
foreach ($line in Get-Content -LiteralPath $checksumPath) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') {
        throw "Invalid SHA256SUMS entry: $line"
    }
    $checksumEntries[$Matches[2]] = $Matches[1]
}

$checksumFiles = @(
    Get-ChildItem -LiteralPath $PackagePath -Recurse -File |
        Where-Object FullName -NE $checksumPath
)
foreach ($file in $checksumFiles) {
    $relativePath = [System.IO.Path]::GetRelativePath($PackagePath, $file.FullName).Replace('\', '/')
    $actualHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($checksumEntries[$relativePath] -ne $actualHash) {
        throw "SHA256SUMS verification failed for $relativePath."
    }
}

if ($checksumEntries.Count -ne $checksumFiles.Count) {
    throw 'SHA256SUMS contains missing or unexpected entries.'
}

[pscustomobject]@{
    product = $manifest.product
    version = $manifest.version
    sourceCommit = $manifest.source.commit
    signingStatus = $manifest.signing.status
    publisher = $manifest.signing.publisher
    verifiedFiles = $manifest.files.Count
    result = 'Verified'
}
