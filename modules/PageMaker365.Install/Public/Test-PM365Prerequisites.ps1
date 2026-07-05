function Test-PM365Prerequisites {
    [CmdletBinding()]
    param()

    $pwshVersion = $PSVersionTable.PSVersion.ToString()
    $bicepCommand = Get-PM365BicepCommand
    $azModule = Get-Module -ListAvailable -Name Az.Accounts | Select-Object -First 1

    $results = @()
    $results += New-PM365Result `
        -Status 'Passed' `
        -Code 'PowerShellReady' `
        -Summary "PowerShell $pwshVersion is available." `
        -Details 'The installer requires PowerShell 7 or later.'

    if ($azModule) {
        $results += New-PM365Result `
            -Status 'Passed' `
            -Code 'AzAccountsReady' `
            -Summary "Az.Accounts $($azModule.Version) is available." `
            -Details 'Azure authentication commands can be loaded.'
    } else {
        $results += New-PM365Result `
            -Status 'Warning' `
            -Code 'AzAccountsMissing' `
            -Summary 'Az.Accounts is not installed.' `
            -Details 'The real installer will require the Az PowerShell modules before deployment.'
    }

    if ($bicepCommand) {
        $bicepVersion = (& $bicepCommand --version 2>$null) -join ' '
        $results += New-PM365Result `
            -Status 'Passed' `
            -Code 'BicepReady' `
            -Summary 'Bicep is available.' `
            -Details "$bicepVersion ($bicepCommand)"
    } else {
        $results += New-PM365Result `
            -Status 'Warning' `
            -Code 'BicepMissing' `
            -Summary 'Bicep is not available on PATH.' `
            -Details 'The desktop installer will surface this as a prerequisite warning until Bicep deployment is wired.'
    }

    $results
}
