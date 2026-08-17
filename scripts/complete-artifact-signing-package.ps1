[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('CreateSignedManifest', 'CompleteSignedArchive')]
    [string] $Stage,

    [Parameter(Mandatory)]
    [string] $OutputPath,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [string] $Runtime = 'win-x64',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $ExpectedSourceCommit,

    [Parameter(Mandatory)]
    [string] $ExpectedPublisher,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $ExpectedCertificateThumbprint,

    [string] $TimestampServer = 'http://timestamp.acs.microsoft.com'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$signatureValidationPath = Join-Path $repoRoot 'distribution\ReleaseSignatureValidation.ps1'
if (-not (Test-Path -LiteralPath $signatureValidationPath -PathType Leaf)) {
    throw "Required release signature validation helper is missing: $signatureValidationPath"
}
Add-Type -AssemblyName System.Security.Cryptography.Pkcs
. $signatureValidationPath
$resolvedRepoRoot = (Resolve-Path -LiteralPath $repoRoot).Path.TrimEnd('\')
$OutputPath = (Resolve-Path -LiteralPath $OutputPath).Path
$repoPrefix = "$resolvedRepoRoot\"
if (-not $OutputPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Package output must stay inside the repository. Requested path: $OutputPath"
}

$normalizedThumbprint = $ExpectedCertificateThumbprint.ToUpperInvariant()
$manifestPath = Join-Path $OutputPath 'release-manifest.json'
$manifestSignaturePath = "$manifestPath.p7s"
$checksumPath = Join-Path $OutputPath 'SHA256SUMS.txt'
$archivePath = "$OutputPath.zip"
$archiveChecksumPath = "$archivePath.sha256"

function Get-PM365SourceIdentity {
    Push-Location $repoRoot
    try {
        $commit = (git rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve the source commit.' }

        $committedAt = (git show -s --format=%cI HEAD).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve the source commit timestamp.' }

        [pscustomobject]@{
            commit = $commit
            committedAt = $committedAt
            dirty = @((git status --porcelain)).Count -gt 0
        }
    }
    finally {
        Pop-Location
    }
}

function Get-PM365PayloadFiles {
    @(
        Get-ChildItem -LiteralPath $OutputPath -Recurse -File |
            Where-Object {
                $_.FullName -notin @($manifestPath, $manifestSignaturePath, $checksumPath)
            } |
            Sort-Object FullName
    )
}

function Test-PM365SignatureRequired {
    param([Parameter(Mandatory)] [System.IO.FileInfo] $File)

    $relativePath = [System.IO.Path]::GetRelativePath($OutputPath, $File.FullName).Replace('\', '/')
    return (
        $relativePath -eq 'app/PageMaker365.Installer.exe' -or
        $relativePath -like 'app/PageMaker365.Installer*.dll' -or
        $File.Extension -in @('.ps1', '.psm1', '.psd1')
    )
}

function Assert-PM365ArtifactSigningSignature {
    param([Parameter(Mandatory)] [System.IO.FileInfo] $File)

    $signature = Get-AuthenticodeSignature -LiteralPath $File.FullName
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate) {
        throw "Artifact Signing verification failed for '$($File.FullName)': $($signature.StatusMessage)"
    }
    if ($signature.SignerCertificate.Subject -ne $ExpectedPublisher -or
        $signature.SignerCertificate.Thumbprint.ToUpperInvariant() -ne $normalizedThumbprint) {
        throw "Artifact Signing publisher verification failed for '$($File.FullName)'."
    }
    if ($null -eq $signature.TimeStamperCertificate) {
        throw "Artifact Signing timestamp verification failed for '$($File.FullName)'."
    }
}

function Assert-PM365DetachedManifestSignature {
    if (-not (Test-Path -LiteralPath $manifestSignaturePath -PathType Leaf)) {
        throw "Detached manifest signature is missing: $manifestSignaturePath"
    }

    $content = [System.Security.Cryptography.Pkcs.ContentInfo]::new(
        [System.IO.File]::ReadAllBytes($manifestPath))
    $signedManifest = [System.Security.Cryptography.Pkcs.SignedCms]::new($content, $true)
    try {
        $signedManifest.Decode([System.IO.File]::ReadAllBytes($manifestSignaturePath))
        $signedManifest.CheckSignature($true)
    }
    catch {
        throw 'Detached release-manifest signature verification failed.'
    }

    if ($signedManifest.SignerInfos.Count -ne 1 -or
        $null -eq $signedManifest.SignerInfos[0].Certificate -or
        $signedManifest.SignerInfos[0].Certificate.Subject -ne $ExpectedPublisher -or
        $signedManifest.SignerInfos[0].Certificate.Thumbprint.ToUpperInvariant() -ne $normalizedThumbprint) {
        throw 'The detached release-manifest signer does not match the approved Artifact Signing identity.'
    }

    Assert-PM365Rfc3161TimestampForSignerInfo `
        -SignerInfo $signedManifest.SignerInfos[0] `
        -Context 'the detached release-manifest signature' | Out-Null
}

function New-PM365Archive {
    if ((Test-Path -LiteralPath $archivePath) -or (Test-Path -LiteralPath $archiveChecksumPath)) {
        throw 'The signed archive or its checksum already exists. Start from a newly prepared package directory.'
    }

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
    return $archiveSha256
}

if ($Stage -eq 'CreateSignedManifest') {
    foreach ($path in @($manifestPath, $manifestSignaturePath, $checksumPath, $archivePath, $archiveChecksumPath)) {
        if (Test-Path -LiteralPath $path) {
            throw "Expected a freshly prepared signing payload, but release evidence already exists: $path"
        }
    }

    $source = Get-PM365SourceIdentity
    if ($source.dirty) {
        throw 'Signed release packaging requires a clean Git worktree.'
    }
    if ($source.commit -ne $ExpectedSourceCommit.ToLowerInvariant()) {
        throw "Checked-out source commit '$($source.commit)' does not match the approved workflow commit '$ExpectedSourceCommit'."
    }

    $payloadFiles = Get-PM365PayloadFiles
    $requiredFiles = @($payloadFiles | Where-Object { Test-PM365SignatureRequired $_ })
    if ($requiredFiles.Count -eq 0) {
        throw 'The prepared payload does not contain any required signing targets.'
    }
    foreach ($file in $requiredFiles) {
        Assert-PM365ArtifactSigningSignature -File $file
    }

    $manifestFiles = @(
        foreach ($file in $payloadFiles) {
            [ordered]@{
                path = [System.IO.Path]::GetRelativePath($OutputPath, $file.FullName).Replace('\', '/')
                length = $file.Length
                sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                signatureRequired = Test-PM365SignatureRequired -File $file
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
            commit = $source.commit
            committedAt = $source.committedAt
            dirty = $source.dirty
        }
        signing = [ordered]@{
            status = 'Signed'
            requiredForCustomerRelease = $true
            publisher = $ExpectedPublisher
            certificateThumbprint = $normalizedThumbprint
            timestampServer = $TimestampServer
        }
        files = $manifestFiles
    }
    $manifest | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

    [pscustomobject]@{
        manifestPath = $manifestPath
        manifestSignaturePath = $manifestSignaturePath
        signingTargets = $requiredFiles.Count
        sourceCommit = $source.commit
        stage = $Stage
    }
    return
}

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Signed release manifest is missing: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.version -ne $Version -or
    $manifest.source.commit -ne $ExpectedSourceCommit.ToLowerInvariant() -or
    $manifest.signing.status -ne 'Signed' -or
    $manifest.signing.publisher -ne $ExpectedPublisher -or
    ([string]$manifest.signing.certificateThumbprint).ToUpperInvariant() -ne $normalizedThumbprint) {
    throw 'The signed release manifest does not match the approved workflow identity.'
}

Assert-PM365DetachedManifestSignature

$payloadFiles = Get-PM365PayloadFiles
$actualPayloadPaths = @(
    $payloadFiles |
        ForEach-Object { [System.IO.Path]::GetRelativePath($OutputPath, $_.FullName).Replace('\', '/') } |
        Sort-Object
)
$manifestPayloadPaths = @($manifest.files | ForEach-Object { [string]$_.path } | Sort-Object)
if (($actualPayloadPaths -join "`n") -ne ($manifestPayloadPaths -join "`n")) {
    throw 'The payload inventory changed after its detached manifest was signed.'
}

foreach ($file in $payloadFiles | Where-Object { Test-PM365SignatureRequired $_ }) {
    Assert-PM365ArtifactSigningSignature -File $file
}

$archiveSha256 = New-PM365Archive
[pscustomobject]@{
    archivePath = $archivePath
    archiveChecksumPath = $archiveChecksumPath
    archiveSha256 = $archiveSha256
    manifestPath = $manifestPath
    manifestSignaturePath = $manifestSignaturePath
    stage = $Stage
}
