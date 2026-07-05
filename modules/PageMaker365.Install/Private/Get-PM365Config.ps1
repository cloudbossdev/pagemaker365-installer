function Get-PM365Config {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath
    )

    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        throw "Customer install package was not found: $ConfigPath"
    }

    Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
}

