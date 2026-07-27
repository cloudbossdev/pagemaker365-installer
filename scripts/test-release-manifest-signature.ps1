[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

$packageSource = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts\package.ps1') -Raw
$verifierSource = Get-Content -LiteralPath (Join-Path $repoRoot 'distribution\Verify-PageMaker365Installer.ps1') -Raw
foreach ($requiredSource in @($packageSource, $verifierSource)) {
    Assert-True ($requiredSource.Contains('$manifestSignaturePath = "$manifestPath.p7s"')) 'Release manifest detached-signature path is missing.'
    Assert-True ($requiredSource.Contains('System.Security.Cryptography.Pkcs.SignedCms')) 'Release manifest CMS implementation is missing.'
}

Add-Type -AssemblyName System.Security.Cryptography.Pkcs
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

Write-Host 'Detached release-manifest signature tests passed.'
