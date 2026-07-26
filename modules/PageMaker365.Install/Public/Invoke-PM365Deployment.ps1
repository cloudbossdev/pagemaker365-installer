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

    $parameterValidationIssues = @(Get-PM365TemplateParameterValidationIssue -Config $config)
    if ($parameterValidationIssues.Count -gt 0) {
        $artifactPath = ''
        $details = @($parameterValidationIssues | ForEach-Object { "{0}: {1}" -f $_.field, $_.message }) -join [Environment]::NewLine
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365DeploymentArtifact `
                -Config $config `
                -Status 'Failed' `
                -ErrorCode 'DeploymentParameterValidationFailed' `
                -ErrorMessage $details
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-result.json' `
                -InputObject $artifact
        }

        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $details = "$details$([Environment]::NewLine)Artifact: $artifactPath"
        }

        New-PM365Result `
            -Status 'Failed' `
            -Code 'DeploymentParameterValidationFailed' `
            -Summary 'Azure deployment parameters are missing or invalid.' `
            -Details $details `
            -RetrySafe $false `
            -Data @{
                issues = @($parameterValidationIssues)
                artifactPath = $artifactPath
            }
        return
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

    $expectedSubscriptionId = [string]$config.azure.subscriptionId
    $actualSubscriptionId = [string]$context.Subscription.Id
    if (-not [string]::IsNullOrWhiteSpace($expectedSubscriptionId) -and $expectedSubscriptionId -ne $actualSubscriptionId) {
        $artifactPath = ''
        $details = "Current Azure subscription '$actualSubscriptionId' does not match package subscription '$expectedSubscriptionId'. Select the package subscription before running deployment."
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365DeploymentArtifact `
                -Config $config `
                -Context $context `
                -Status 'Failed' `
                -ErrorCode 'AzureSubscriptionMismatch' `
                -ErrorMessage $details
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-result.json' `
                -InputObject $artifact
        }

        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $details = "$details$([Environment]::NewLine)Artifact: $artifactPath"
        }

        $data = @{}
        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $data.artifactPath = $artifactPath
        }

        New-PM365Result `
            -Status 'Failed' `
            -Code 'AzureSubscriptionMismatch' `
            -Summary 'Azure subscription context does not match the customer package.' `
            -Details $details `
            -RetrySafe $true `
            -Data $data
        return
    }

    $resourceGroupName = [string]$config.azure.resourceGroupName
    $resourceGroup = Get-AzResourceGroup -Name $resourceGroupName -ErrorAction SilentlyContinue
    if ($resourceGroup) {
        $productTag = [string](Get-PM365ObjectProperty -InputObject $resourceGroup.Tags -Name @('product'))
        $managedByTag = [string](Get-PM365ObjectProperty -InputObject $resourceGroup.Tags -Name @('managedBy'))
    }
    if ($resourceGroup -and ($productTag -ne 'PageMaker365' -or $managedByTag -ne 'PageMaker365')) {
        $artifactPath = ''
        $details = "Resource group '$resourceGroupName' exists but does not have the required product=PageMaker365 and managedBy=PageMaker365 ownership tags."
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365DeploymentArtifact `
                -Config $config `
                -Context $context `
                -Status 'Failed' `
                -ErrorCode 'AzureResourceGroupOwnershipMismatch' `
                -ErrorMessage $details
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-result.json' `
                -InputObject $artifact
        }

        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $details = "$details$([Environment]::NewLine)Artifact: $artifactPath"
        }

        $data = @{}
        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $data.artifactPath = $artifactPath
        }

        New-PM365Result `
            -Status 'Failed' `
            -Code 'AzureResourceGroupOwnershipMismatch' `
            -Summary 'The existing target resource group is not owned by PageMaker365.' `
            -Details $details `
            -RetrySafe $false `
            -Data $data
        return
    }

    $parameters = New-PM365TemplateParameterObject -Config $config
    $deployment = $null

    try {
        $deployment = New-AzSubscriptionDeployment `
            -Location ([string]$config.azure.location) `
            -TemplateFile $TemplateFile `
            -TemplateParameterObject $parameters `
            -ErrorAction Stop
    } catch {
        $rawErrorMessage = $_.Exception.Message
        $isAppServiceCapacityFailure = (
            $rawErrorMessage -match '(?i)No available instances to satisfy this request' -or
            $rawErrorMessage -match '(?i)ExtendedCode[\"''\s:]+03029'
        )
        $errorCode = if ($isAppServiceCapacityFailure) { 'AppServiceCapacityUnavailable' } else { 'AzureDeploymentFailed' }
        $errorSummary = if ($isAppServiceCapacityFailure) {
            'Azure App Service capacity is temporarily unavailable.'
        } else {
            'Azure deployment failed.'
        }
        $artifactPath = ''
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365DeploymentArtifact `
                -Config $config `
                -Context $context `
                -Deployment $deployment `
                -Status 'Failed' `
                -ErrorCode $errorCode `
                -ErrorMessage $rawErrorMessage
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-result.json' `
                -InputObject $artifact
        }

        $details = $rawErrorMessage
        if ($isAppServiceCapacityFailure) {
            $correlationId = ''
            if ($rawErrorMessage -match '(?i)CorrelationId:\s*([0-9a-f-]{36})') {
                $correlationId = $Matches[1]
            }

            $details = "Azure could not allocate the requested App Service plan in $([string]$config.azure.location). The plan now requests asynchronous allocation. Run Deployment Preview again, approve the updated preview, and retry Install. Existing successfully created resources will be reused."
            if (-not [string]::IsNullOrWhiteSpace($correlationId)) {
                $details = "$details$([Environment]::NewLine)Azure correlation ID: $correlationId"
            }
        }
        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $details = "$details$([Environment]::NewLine)Artifact: $artifactPath"
        }
        $data = @{}
        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $data.artifactPath = $artifactPath
        }

        New-PM365Result `
            -Status 'Failed' `
            -Code $errorCode `
            -Summary $errorSummary `
            -Details $details `
            -RetrySafe $true `
            -Data $data
        return
    }

    $operations = @()
    $operationCommand = Get-Command -Name Get-AzSubscriptionDeploymentOperation -ErrorAction SilentlyContinue
    if ($operationCommand -and -not [string]::IsNullOrWhiteSpace([string]$deployment.DeploymentName)) {
        try {
            $operations = @(
                Get-AzSubscriptionDeploymentOperation `
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
