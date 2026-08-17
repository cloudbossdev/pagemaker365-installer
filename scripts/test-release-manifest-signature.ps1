[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

$completionSource = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts\complete-artifact-signing-package.ps1') -Raw
$verifierSource = Get-Content -LiteralPath (Join-Path $repoRoot 'distribution\Verify-PageMaker365Installer.ps1') -Raw
foreach ($requiredSource in @($completionSource, $verifierSource)) {
    Assert-True ($requiredSource.Contains('$manifestSignaturePath = "$manifestPath.p7s"')) 'Release manifest detached-signature path is missing.'
    Assert-True ($requiredSource.Contains('System.Security.Cryptography.Pkcs.SignedCms')) 'Release manifest CMS implementation is missing.'
    Assert-True ($requiredSource.Contains('Assert-PM365Rfc3161TimestampForSignerInfo')) 'Release manifest RFC 3161 timestamp validation is missing.'
}

Add-Type -AssemblyName System.Security.Cryptography.Pkcs
. (Join-Path $repoRoot 'distribution\ReleaseSignatureValidation.ps1')

function Get-Rfc3161FixtureBytes {
    # The source vector is dotnet/runtime's IndefiniteLengthContentDocument
    # (MIT License):
    # https://github.com/dotnet/runtime/blob/main/src/libraries/System.Security.Cryptography.Pkcs/tests/SignedCms/SignedDocuments.cs
    # The fixture is gzip+base64 encoded only to keep this repository text-only.
    $fixturePath = Join-Path $repoRoot 'scripts\test-fixtures\rfc3161-dotnet-runtime-signed-cms.b64'
    $compressedBytes = [Convert]::FromBase64String(
        (Get-Content -LiteralPath $fixturePath -Raw).Trim())
    $compressedStream = [System.IO.MemoryStream]::new($compressedBytes, $false)
    $gzipStream = [System.IO.Compression.GZipStream]::new(
        $compressedStream,
        [System.IO.Compression.CompressionMode]::Decompress)
    $expandedStream = [System.IO.MemoryStream]::new()
    try {
        $gzipStream.CopyTo($expandedStream)
        return $expandedStream.ToArray()
    }
    finally {
        $gzipStream.Dispose()
        $compressedStream.Dispose()
        $expandedStream.Dispose()
    }
}

function Find-ExactByteSequenceOffsets {
    param(
        [Parameter(Mandatory)] [byte[]] $Bytes,
        [Parameter(Mandatory)] [byte[]] $Sequence
    )

    $offsets = [System.Collections.Generic.List[int]]::new()
    for ($offset = 0; $offset -le $Bytes.Length - $Sequence.Length; $offset++) {
        $isMatch = $true
        for ($index = 0; $index -lt $Sequence.Length; $index++) {
            if ($Bytes[$offset + $index] -ne $Sequence[$index]) {
                $isMatch = $false
                break
            }
        }
        if ($isMatch) { $offsets.Add($offset) }
    }
    return $offsets.ToArray()
}

function Assert-ThrowsTimestampFailure {
    param([Parameter(Mandatory)] [scriptblock] $Action)
    try {
        & $Action | Out-Null
    }
    catch {
        return
    }
    throw 'RFC 3161 timestamp validation accepted a token bound to a different signer signature.'
}

$rfc3161Vector = Get-Rfc3161FixtureBytes
Assert-True ($rfc3161Vector.Length -eq 9814) 'The RFC 3161 upstream test vector length changed unexpectedly.'
Assert-True (
    ([Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($rfc3161Vector)) -eq
        '4F55FC1BDF5031D5E0CD7FD5FF4EA8C93CE2CBC8313EC1860BD6BE5E16A150D0')) `
    'The RFC 3161 upstream test vector hash does not match its pinned value.'

$rfc3161SignedCms = [System.Security.Cryptography.Pkcs.SignedCms]::new()
$rfc3161SignedCms.Decode($rfc3161Vector)
Assert-True ($rfc3161SignedCms.SignerInfos.Count -eq 1) 'The RFC 3161 upstream test vector must contain one manifest signer.'
Assert-PM365Rfc3161TimestampForSignerInfo `
    -SignerInfo $rfc3161SignedCms.SignerInfos[0] `
    -Context 'the known-good RFC 3161 upstream test vector' | Out-Null

$originalSignature = $rfc3161SignedCms.SignerInfos[0].GetSignature()
$signatureOffsets = Find-ExactByteSequenceOffsets -Bytes $rfc3161Vector -Sequence $originalSignature
Assert-True ($signatureOffsets.Count -eq 1) 'The RFC 3161 upstream test vector must contain one primary signer signature value.'
$forgedVector = [byte[]]$rfc3161Vector.Clone()
$forgedVector[$signatureOffsets[0] + $originalSignature.Length - 1] = `
    $forgedVector[$signatureOffsets[0] + $originalSignature.Length - 1] -bxor 0x01
$forgedSignedCms = [System.Security.Cryptography.Pkcs.SignedCms]::new()
$forgedSignedCms.Decode($forgedVector)
Assert-True (
    -not [System.Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
        $originalSignature,
        $forgedSignedCms.SignerInfos[0].GetSignature())) `
    'The forged RFC 3161 test vector did not alter the signer signature value.'
Assert-ThrowsTimestampFailure {
    Assert-PM365Rfc3161TimestampForSignerInfo `
        -SignerInfo $forgedSignedCms.SignerInfos[0] `
        -Context 'a forged RFC 3161 test vector'
}

$rsa = [System.Security.Cryptography.RSA]::Create(2048)
$certificate = $null
try {
    $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=PageMaker365 Manifest Contract Test',
        $rsa,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $certificate = $request.CreateSelfSigned(
        [DateTimeOffset]::UtcNow.AddMinutes(-5),
        [DateTimeOffset]::UtcNow.AddDays(1))

    $manifestBytes = [System.Text.Encoding]::UTF8.GetBytes('{"product":"PageMaker365 Installer","version":"contract-test"}')
    $content = [System.Security.Cryptography.Pkcs.ContentInfo]::new($manifestBytes)
    $signedManifest = [System.Security.Cryptography.Pkcs.SignedCms]::new($content, $true)
    $signer = [System.Security.Cryptography.Pkcs.CmsSigner]::new($certificate)
    $signer.IncludeOption = [System.Security.Cryptography.X509Certificates.X509IncludeOption]::EndCertOnly
    $signedManifest.ComputeSignature($signer, $true)
    $signatureBytes = $signedManifest.Encode()

    $verifiedManifest = [System.Security.Cryptography.Pkcs.SignedCms]::new(
        [System.Security.Cryptography.Pkcs.ContentInfo]::new($manifestBytes),
        $true)
    $verifiedManifest.Decode($signatureBytes)
    $verifiedManifest.CheckSignature($true)
    Assert-True ($verifiedManifest.SignerInfos.Count -eq 1) 'Detached manifest must have exactly one signer.'
    Assert-True (
        $verifiedManifest.SignerInfos[0].Certificate.Thumbprint -eq $certificate.Thumbprint) `
        'Detached manifest signer identity does not match the signing certificate.'

    $timestampMissingRejected = $false
    try {
        Assert-PM365Rfc3161TimestampForSignerInfo `
            -SignerInfo $verifiedManifest.SignerInfos[0] `
            -Context 'a detached manifest without a timestamp' | Out-Null
    }
    catch {
        $timestampMissingRejected = $true
    }
    Assert-True $timestampMissingRejected 'RFC 3161 validation accepted a detached manifest without a timestamp token.'

    $tampered = [System.Text.Encoding]::UTF8.GetBytes('{"product":"PageMaker365 Installer","version":"tampered"}')
    $tamperRejected = $false
    try {
        $tamperedManifest = [System.Security.Cryptography.Pkcs.SignedCms]::new(
            [System.Security.Cryptography.Pkcs.ContentInfo]::new($tampered),
            $true)
        $tamperedManifest.Decode($signatureBytes)
        $tamperedManifest.CheckSignature($true)
    }
    catch {
        $tamperRejected = $true
    }
    Assert-True $tamperRejected 'Detached manifest signature accepted modified content.'
}
finally {
    if ($certificate) { $certificate.Dispose() }
    $rsa.Dispose()
}

Write-Host 'Detached release-manifest and RFC 3161 signature tests passed.'
