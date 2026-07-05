function Start-PM365MockInstall {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath
    )

    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        New-PM365Result `
            -Status 'Failed' `
            -Code 'ConfigMissing' `
            -Summary 'Customer install package was not found.' `
            -Details $ConfigPath `
            -RetrySafe $false
        return
    }

    $config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json

    New-PM365Result `
        -Status 'Passed' `
        -Code 'ConfigLoaded' `
        -Summary "Loaded install package for $($config.customer.tenantName)." `
        -Details $ConfigPath

    Test-PM365Prerequisites

    New-PM365Result `
        -Status 'Failed' `
        -Code 'MissingApplicationAdministrator' `
        -Summary 'The signed-in user may not be able to approve app permissions.' `
        -Details 'Ask a Global Administrator, Cloud Application Administrator, or Application Administrator to complete consent.' `
        -RetrySafe $false
}

