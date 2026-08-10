function Get-PM365BoundConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath,

        [Parameter(Mandatory)]
        [ValidatePattern('^[0-9a-f]{64}$')]
        [string] $ExpectedPackagePayloadSha256
    )

    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
        throw [System.IO.FileNotFoundException]::new("Customer install package was not found: $ConfigPath")
    }

    $resolvedPath = (Resolve-Path -LiteralPath $ConfigPath -ErrorAction Stop).ProviderPath
    $payload = [System.IO.File]::ReadAllBytes($resolvedPath)
    try {
        $actualHash = [Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData($payload)).ToLowerInvariant()
        if (-not [string]::Equals(
            $actualHash,
            $ExpectedPackagePayloadSha256,
            [System.StringComparison]::Ordinal)) {
            throw [System.IO.InvalidDataException]::new(
                'The customer package changed after cryptographic validation. Reload and validate the exact package before continuing.')
        }

        $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
        $packageJson = $utf8.GetString($payload)
        try {
            $packageJson | ConvertFrom-Json -Depth 100 -ErrorAction Stop
        } catch {
            throw [System.IO.InvalidDataException]::new(
                'The exact validated customer package payload is not valid JSON.',
                $_.Exception)
        }
    } finally {
        if ($payload.Length -gt 0) {
            [System.Security.Cryptography.CryptographicOperations]::ZeroMemory($payload)
        }
    }
}
