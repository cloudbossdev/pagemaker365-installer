function Invoke-PM365Deployment {
    [CmdletBinding(SupportsShouldProcess)]
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

    if (-not $PSCmdlet.ShouldProcess([string]$config.azure.resourceGroupName, 'Deploy PageMaker365 Azure resources')) {
        New-PM365Result `
            -Status 'Skipped' `
            -Code 'DeploymentSkipped' `
            -Summary 'Azure deployment was skipped.' `
            -Details 'The deployment command requires explicit approval.' `
            -RetrySafe $true
        return
    }

    Import-Module Az.Accounts -ErrorAction Stop
    Import-Module Az.Resources -ErrorAction Stop
    $context = Get-AzContext -ErrorAction SilentlyContinue
    if (-not $context -or -not $context.Subscription.Id) {
        New-PM365Result `
            -Status 'Warning' `
            -Code 'AzureSubscriptionUnavailable' `
            -Summary 'Azure subscription context is required before deployment.' `
            -Details 'Run Set-AzContext with the target subscription before deployment.' `
            -RetrySafe $true
        return
    }

    $parameters = New-PM365TemplateParameterObject -Config $config

    try {
        $deployment = New-AzResourceGroupDeployment `
            -ResourceGroupName ([string]$config.azure.resourceGroupName) `
            -TemplateFile $TemplateFile `
            -TemplateParameterObject $parameters `
            -ErrorAction Stop

        New-PM365Result `
            -Status 'Passed' `
            -Code 'AzureDeploymentReady' `
            -Summary 'Azure deployment completed.' `
            -Details $deployment.DeploymentName `
            -Data @{ provisioningState = [string]$deployment.ProvisioningState }
    } catch {
        New-PM365Result `
            -Status 'Failed' `
            -Code 'AzureDeploymentFailed' `
            -Summary 'Azure deployment failed.' `
            -Details $_.Exception.Message `
            -RetrySafe $true
    }
}
