function Assert-PM365Rfc3161TimestampForSignerInfo {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [System.Security.Cryptography.Pkcs.SignerInfo] $SignerInfo,

        [Parameter(Mandatory)]
        [string] $Context
    )

    Add-Type -AssemblyName System.Security.Cryptography.Pkcs
    $timestampAttributeOid = '1.2.840.113549.1.9.16.2.14'
    $timestampAttributes = @(
        $SignerInfo.UnsignedAttributes |
            Where-Object { $_.Oid.Value -eq $timestampAttributeOid }
    )
    if ($timestampAttributes.Count -ne 1 -or $timestampAttributes[0].Values.Count -ne 1) {
        throw "RFC 3161 timestamp evidence is missing or ambiguous for $Context."
    }

    $encodedToken = [byte[]]$timestampAttributes[0].Values[0].RawData
    if ($encodedToken.Length -eq 0) {
        throw "RFC 3161 timestamp evidence is empty for $Context."
    }

    $timestampToken = $null
    $bytesConsumed = 0
    try {
        $decoded = [System.Security.Cryptography.Pkcs.Rfc3161TimestampToken]::TryDecode(
            [System.ReadOnlyMemory[byte]]::new($encodedToken),
            [ref]$timestampToken,
            [ref]$bytesConsumed)
    }
    catch {
        throw "RFC 3161 timestamp token decoding failed for $Context."
    }
    if (-not $decoded -or $null -eq $timestampToken -or $bytesConsumed -ne $encodedToken.Length) {
        throw "RFC 3161 timestamp token decoding failed for $Context."
    }

    $timestampCertificate = $null
    $extraStore = [System.Security.Cryptography.X509Certificates.X509Certificate2Collection]::new()
    try {
        $boundToSignature = $timestampToken.VerifySignatureForSignerInfo(
            $SignerInfo,
            [ref]$timestampCertificate,
            $extraStore)
    }
    catch {
        throw "RFC 3161 timestamp token cryptographic verification failed for $Context."
    }
    if (-not $boundToSignature -or $null -eq $timestampCertificate) {
        throw "RFC 3161 timestamp token is not cryptographically bound to $Context."
    }

    [pscustomobject]@{
        timestamp = $timestampToken.TokenInfo.Timestamp
        timestampAuthority = $timestampCertificate.Subject
    }
}
