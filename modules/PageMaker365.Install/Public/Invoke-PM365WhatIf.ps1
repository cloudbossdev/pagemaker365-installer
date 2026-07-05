function Invoke-PM365WhatIf {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath,

        [string] $TemplateFile = (Get-PM365DefaultTemplateFile)
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $buildResult = Invoke-PM365BicepBuild -TemplateFile $TemplateFile
    if ($buildResult.status -eq 'Failed') {
        return $buildResult
    }

    $bicepPath = Get-PM365BicepCommand
    if ($bicepPath) {
        $bicepDirectory = Split-Path -Parent $bicepPath
        if ($env:Path -notlike "*$bicepDirectory*") {
            $env:Path = "$bicepDirectory;$env:Path"
        }
    }

    $azResources = Get-Module -ListAvailable -Name Az.Resources | Select-Object -First 1
    if (-not $azResources) {
        New-PM365Result `
            -Status 'Warning' `
            -Code 'AzResourcesMissing' `
            -Summary 'Az.Resources is required for Azure what-if.' `
            -Details 'Install Az.Resources before running deployment preview.' `
            -RetrySafe $true
        return
    }

    Import-Module Az.Accounts -ErrorAction Stop
    Import-Module Az.Resources -ErrorAction Stop
    $context = Get-AzContext -ErrorAction SilentlyContinue
    if (-not $context) {
        New-PM365Result `
            -Status 'Warning' `
            -Code 'AzureNotSignedIn' `
            -Summary 'Azure sign-in is required before running what-if.' `
            -Details 'Sign in and select the target subscription before deployment preview.' `
            -RetrySafe $true
        return
    }

    if (-not $context.Subscription.Id) {
        New-PM365Result `
            -Status 'Warning' `
            -Code 'AzureSubscriptionUnavailable' `
            -Summary 'Azure subscription context is required before running what-if.' `
            -Details 'Run Set-AzContext with the target subscription before deployment preview.' `
            -RetrySafe $true
        return
    }

    $parameters = New-PM365TemplateParameterObject -Config $config
    try {
        $whatIf = New-AzResourceGroupDeployment `
            -ResourceGroupName ([string]$config.azure.resourceGroupName) `
            -TemplateFile $TemplateFile `
            -TemplateParameterObject $parameters `
            -WhatIf `
            -ErrorAction Stop `
            2>&1

        New-PM365Result `
            -Status 'Passed' `
            -Code 'AzureWhatIfReady' `
            -Summary 'Azure deployment preview completed.' `
            -Details ($whatIf -join [Environment]::NewLine)
    } catch {
        New-PM365Result `
            -Status 'Failed' `
            -Code 'AzureWhatIfFailed' `
            -Summary 'Azure deployment preview failed.' `
            -Details $_.Exception.Message `
            -RetrySafe $true
    }
}
