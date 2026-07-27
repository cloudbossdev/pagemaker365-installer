function Test-PM365Prerequisites {
    [CmdletBinding()]
    param()

    $pwshVersion = $PSVersionTable.PSVersion.ToString()
    $bicepCommand = Get-PM365BicepCommand
    $azModule = Get-Module -ListAvailable -Name Az.Accounts | Select-Object -First 1
    $azWebsitesModule = Get-Module -ListAvailable -Name Az.Websites | Select-Object -First 1

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
            -Status 'Failed' `
            -Code 'AzAccountsMissing' `
            -Summary 'Az.Accounts is not installed.' `
            -Details 'Install Az.Accounts before running Azure sign-in or preflight.' `
            -RetrySafe $true
    }

    if ($azWebsitesModule) {
        $results += New-PM365Result `
            -Status 'Passed' `
            -Code 'AzWebsitesReady' `
            -Summary "Az.Websites $($azWebsitesModule.Version) is available." `
            -Details 'Ready-to-run API and portal ZIP files can be deployed to App Service.'
    } else {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'AzWebsitesMissing' `
            -Summary 'Az.Websites is not installed.' `
            -Details 'Install Az.Websites before running PageMaker365 deployment.' `
            -RetrySafe $true
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
            -Status 'Failed' `
            -Code 'BicepMissing' `
            -Summary 'Bicep is not available on PATH.' `
            -Details 'Install the Bicep CLI before running deployment preview or install.' `
            -RetrySafe $true
    }

    $results
}
