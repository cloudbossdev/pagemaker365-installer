function Invoke-PM365Deployment {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath,

        [string] $TemplateFile = (Get-PM365DefaultTemplateFile),

        [string] $OutputPath = ''
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $buildResult = Invoke-PM365BicepBuild -TemplateFile $TemplateFile
    if ($buildResult.status -eq 'Failed') {
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365DeploymentArtifact `
                -Config $config `
                -Status 'Failed' `
                -ErrorCode ([string]$buildResult.code) `
                -ErrorMessage ([string]$buildResult.details)
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-result.json' `
                -InputObject $artifact
            $buildResult.data = @{
                artifactPath = $artifactPath
            }
        }

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
        $artifactPath = ''
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365DeploymentArtifact `
                -Config $config `
                -Status 'Skipped' `
                -ErrorCode 'DeploymentSkipped' `
                -ErrorMessage 'The deployment command requires explicit approval.'
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-result.json' `
                -InputObject $artifact
        }
        $data = @{}
        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $data.artifactPath = $artifactPath
        }

        New-PM365Result `
            -Status 'Skipped' `
            -Code 'DeploymentSkipped' `
            -Summary 'Azure deployment was skipped.' `
            -Details 'The deployment command requires explicit approval.' `
            -RetrySafe $true `
            -Data $data
        return
    }

    try {
        Import-Module Az.Accounts -ErrorAction Stop
        Import-Module Az.Resources -ErrorAction Stop
    } catch {
        $artifactPath = ''
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365DeploymentArtifact `
                -Config $config `
                -Status 'Warning' `
                -ErrorCode 'AzModuleImportFailed' `
                -ErrorMessage $_.Exception.Message
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-result.json' `
                -InputObject $artifact
        }
        $data = @{}
        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $data.artifactPath = $artifactPath
        }

        New-PM365Result `
            -Status 'Warning' `
            -Code 'AzModuleImportFailed' `
            -Summary 'Azure PowerShell modules could not be loaded for deployment.' `
            -Details $_.Exception.Message `
            -RetrySafe $true `
            -Data $data
        return
    }

    $context = Get-AzContext -ErrorAction SilentlyContinue
    if (-not $context -or -not $context.Subscription.Id) {
        $artifactPath = ''
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365DeploymentArtifact `
                -Config $config `
                -Context $context `
                -Status 'Warning' `
                -ErrorCode 'AzureSubscriptionUnavailable' `
                -ErrorMessage 'Azure subscription context is required before deployment.'
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-result.json' `
                -InputObject $artifact
        }
        $data = @{}
        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $data.artifactPath = $artifactPath
        }

        New-PM365Result `
            -Status 'Warning' `
            -Code 'AzureSubscriptionUnavailable' `
            -Summary 'Azure subscription context is required before deployment.' `
            -Details 'Run Set-AzContext with the target subscription before deployment.' `
            -RetrySafe $true `
            -Data $data
        return
    }

    $parameters = New-PM365TemplateParameterObject -Config $config
    $deployment = $null

    try {
        $deployment = New-AzResourceGroupDeployment `
            -ResourceGroupName ([string]$config.azure.resourceGroupName) `
            -TemplateFile $TemplateFile `
            -TemplateParameterObject $parameters `
            -ErrorAction Stop
    } catch {
        $artifactPath = ''
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365DeploymentArtifact `
                -Config $config `
                -Context $context `
                -Deployment $deployment `
                -Status 'Failed' `
                -ErrorCode 'AzureDeploymentFailed' `
                -ErrorMessage $_.Exception.Message
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-result.json' `
                -InputObject $artifact
        }

        $details = $_.Exception.Message
        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $details = "$details$([Environment]::NewLine)Artifact: $artifactPath"
        }
        $data = @{}
        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $data.artifactPath = $artifactPath
        }

        New-PM365Result `
            -Status 'Failed' `
            -Code 'AzureDeploymentFailed' `
            -Summary 'Azure deployment failed.' `
            -Details $details `
            -RetrySafe $true `
            -Data $data
        return
    }

    $operations = @()
    $operationCommand = Get-Command -Name Get-AzResourceGroupDeploymentOperation -ErrorAction SilentlyContinue
    if ($operationCommand -and -not [string]::IsNullOrWhiteSpace([string]$deployment.DeploymentName)) {
        try {
            $operations = @(
                Get-AzResourceGroupDeploymentOperation `
                    -ResourceGroupName ([string]$config.azure.resourceGroupName) `
                    -DeploymentName ([string]$deployment.DeploymentName) `
                    -ErrorAction Stop
            )
        } catch {
            $operations = @()
        }
    }

    $artifactPath = ''
    $artifactWriteError = ''
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        try {
            $artifact = New-PM365DeploymentArtifact `
                -Config $config `
                -Context $context `
                -Deployment $deployment `
                -Operations $operations `
                -Status 'Passed'
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-result.json' `
                -InputObject $artifact
        } catch {
            $artifactWriteError = $_.Exception.Message
        }
    }

    $data = @{
        provisioningState = [string]$deployment.ProvisioningState
        deploymentName = [string]$deployment.DeploymentName
    }
    if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
        $data.artifactPath = $artifactPath
    }

    if (-not [string]::IsNullOrWhiteSpace($artifactWriteError)) {
        $data.artifactError = $artifactWriteError
        New-PM365Result `
            -Status 'Warning' `
            -Code 'AzureDeploymentReady' `
            -Summary 'Azure deployment completed, but deployment evidence could not be written.' `
            -Details $artifactWriteError `
            -Data $data
        return
    }

    $details = [string]$deployment.DeploymentName
    if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
        $details = "$details$([Environment]::NewLine)Artifact: $artifactPath"
    }

    New-PM365Result `
        -Status 'Passed' `
        -Code 'AzureDeploymentReady' `
        -Summary 'Azure deployment completed.' `
        -Details $details `
        -Data $data
}
