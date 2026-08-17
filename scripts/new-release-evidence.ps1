[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackagePath,

    [Parameter(Mandatory)]
    [string] $ArchivePath,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $ExpectedPublisher,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $ExpectedCertificateThumbprint,

    [Parameter(Mandatory)]
    [string] $EvidencePath
)

$ErrorActionPreference = 'Stop'
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$ArchivePath = (Resolve-Path -LiteralPath $ArchivePath).Path
$manifestPath = Join-Path $PackagePath 'release-manifest.json'
$manifestSignaturePath = "$manifestPath.p7s"
$checksumPath = Join-Path $PackagePath 'SHA256SUMS.txt'
$archiveChecksumPath = "$ArchivePath.sha256"

foreach ($path in @($manifestPath, $manifestSignaturePath, $checksumPath, $archiveChecksumPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required signed-release evidence is missing: $path"
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.version -ne $Version -or
    $manifest.signing.status -ne 'Signed' -or
    $manifest.signing.publisher -ne $ExpectedPublisher -or
    ([string]$manifest.signing.certificateThumbprint).ToUpperInvariant() -ne $ExpectedCertificateThumbprint.ToUpperInvariant()) {
    throw 'The package manifest does not match the signed-release evidence request.'
}

$signedFiles = @(
    $manifest.files |
        Where-Object { $_.signatureRequired } |
        ForEach-Object {
            [ordered]@{
                path = $_.path
                sha256 = $_.sha256
            }
        }
)
$evidence = [ordered]@{
    schemaVersion = '1.0'
    product = 'PageMaker365 Installer'
    releaseClass = 'internal-release-candidate'
    customerDistributionApproved = $false
    version = $Version
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    source = [ordered]@{
        repository = $manifest.source.repository
        commit = $manifest.source.commit
        committedAt = $manifest.source.committedAt
    }
    signing = [ordered]@{
        provider = 'Azure Artifact Signing'
        publisher = $ExpectedPublisher
        certificateThumbprint = $ExpectedCertificateThumbprint.ToUpperInvariant()
        timestampServer = $manifest.signing.timestampServer
        detachedManifestSignatureSha256 = (Get-FileHash -LiteralPath $manifestSignaturePath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    artifacts = [ordered]@{
        archive = [System.IO.Path]::GetFileName($ArchivePath)
        archiveSha256 = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        archiveChecksum = [System.IO.Path]::GetFileName($archiveChecksumPath)
        manifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        payloadChecksumsSha256 = (Get-FileHash -LiteralPath $checksumPath -Algorithm SHA256).Hash.ToLowerInvariant()
        signedFiles = $signedFiles
    }
    workflow = [ordered]@{
        repository = $env:GITHUB_REPOSITORY
        runId = $env:GITHUB_RUN_ID
        runAttempt = $env:GITHUB_RUN_ATTEMPT
        commit = $env:GITHUB_SHA
        ref = $env:GITHUB_REF
    }
}

$evidence | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $EvidencePath -Encoding utf8NoBOM

[pscustomobject]@{
    evidencePath = $EvidencePath
    archiveSha256 = $evidence.artifacts.archiveSha256
    signedFiles = $signedFiles.Count
}
