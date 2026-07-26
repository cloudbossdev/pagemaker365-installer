function Invoke-PM365WhatIf {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath,

        [string] $TemplateFile = (Get-PM365DefaultTemplateFile),

        [string] $OutputPath = '',

        [switch] $AllowUpgradeTargetStateResume
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $buildResult = Invoke-PM365BicepBuild -TemplateFile $TemplateFile
    if ($buildResult.status -eq 'Failed') {
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $risk = Get-PM365WhatIfRisk -UnstructuredFallback
            $artifact = New-PM365WhatIfArtifact `
                -Config $config `
                -TemplateFile $TemplateFile `
                -Method 'BicepBuild' `
                -Risk $risk `
                -Status 'Failed' `
                -ErrorCode ([string]$buildResult.code) `
                -ErrorMessage ([string]$buildResult.details)
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-whatif.json' `
                -InputObject $artifact
            $buildResult.data = New-PM365WhatIfResultData -Risk $risk -ArtifactPath $artifactPath
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
        $risk = Get-PM365WhatIfRisk -UnstructuredFallback
        $artifactPath = ''
        $details = @($parameterValidationIssues | ForEach-Object { "{0}: {1}" -f $_.field, $_.message }) -join [Environment]::NewLine
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365WhatIfArtifact `
                -Config $config `
                -TemplateFile $TemplateFile `
                -Method 'ParameterValidation' `
                -Risk $risk `
                -Status 'Failed' `
                -ErrorCode 'DeploymentParameterValidationFailed' `
                -ErrorMessage $details
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-whatif.json' `
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

    $azResources = Get-Module -ListAvailable -Name Az.Resources | Select-Object -First 1
    if (-not $azResources) {
        $risk = Get-PM365WhatIfRisk -UnstructuredFallback
        $artifactPath = ''
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365WhatIfArtifact `
                -Config $config `
                -TemplateFile $TemplateFile `
                -Method 'Unavailable' `
                -Risk $risk `
                -Status 'Warning' `
                -ErrorCode 'AzResourcesMissing' `
                -ErrorMessage 'Az.Resources is required for Azure what-if.'
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-whatif.json' `
                -InputObject $artifact
        }

        New-PM365Result `
            -Status 'Warning' `
            -Code 'AzResourcesMissing' `
            -Summary 'Az.Resources is required for Azure what-if.' `
            -Details 'Install Az.Resources before running deployment preview.' `
            -RetrySafe $true `
            -Data (New-PM365WhatIfResultData -Risk $risk -ArtifactPath $artifactPath)
        return
    }

    $azAccounts = Get-Module -ListAvailable -Name Az.Accounts | Select-Object -First 1
    if (-not $azAccounts) {
        $risk = Get-PM365WhatIfRisk -UnstructuredFallback
        $artifactPath = ''
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365WhatIfArtifact `
                -Config $config `
                -TemplateFile $TemplateFile `
                -Method 'Unavailable' `
                -Risk $risk `
                -Status 'Warning' `
                -ErrorCode 'AzAccountsMissing' `
                -ErrorMessage 'Az.Accounts is required for Azure what-if.'
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-whatif.json' `
                -InputObject $artifact
        }

        New-PM365Result `
            -Status 'Warning' `
            -Code 'AzAccountsMissing' `
            -Summary 'Az.Accounts is required for Azure what-if.' `
            -Details 'Install Az.Accounts before running deployment preview.' `
            -RetrySafe $true `
            -Data (New-PM365WhatIfResultData -Risk $risk -ArtifactPath $artifactPath)
        return
    }

    try {
        Import-Module Az.Accounts -ErrorAction Stop
        Import-Module Az.Resources -ErrorAction Stop
    } catch {
        $risk = Get-PM365WhatIfRisk -UnstructuredFallback
        $artifactPath = ''
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365WhatIfArtifact `
                -Config $config `
                -TemplateFile $TemplateFile `
                -Method 'Unavailable' `
                -Risk $risk `
                -Status 'Warning' `
                -ErrorCode 'AzModuleImportFailed' `
                -ErrorMessage $_.Exception.Message
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-whatif.json' `
                -InputObject $artifact
        }

        New-PM365Result `
            -Status 'Warning' `
            -Code 'AzModuleImportFailed' `
            -Summary 'Azure PowerShell modules could not be loaded for what-if.' `
            -Details $_.Exception.Message `
            -RetrySafe $true `
            -Data (New-PM365WhatIfResultData -Risk $risk -ArtifactPath $artifactPath)
        return
    }

    $context = Get-AzContext -ErrorAction SilentlyContinue
    if (-not $context) {
        $risk = Get-PM365WhatIfRisk -UnstructuredFallback
        $artifactPath = ''
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365WhatIfArtifact `
                -Config $config `
                -TemplateFile $TemplateFile `
                -Method 'Unavailable' `
                -Risk $risk `
                -Status 'Warning' `
                -ErrorCode 'AzureNotSignedIn' `
                -ErrorMessage 'Azure sign-in is required before running what-if.'
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-whatif.json' `
                -InputObject $artifact
        }

        New-PM365Result `
            -Status 'Warning' `
            -Code 'AzureNotSignedIn' `
            -Summary 'Azure sign-in is required before running what-if.' `
            -Details 'Sign in and select the target subscription before deployment preview.' `
            -RetrySafe $true `
            -Data (New-PM365WhatIfResultData -Risk $risk -ArtifactPath $artifactPath)
        return
    }

    if (-not $context.Subscription.Id) {
        $risk = Get-PM365WhatIfRisk -UnstructuredFallback
        $artifactPath = ''
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365WhatIfArtifact `
                -Config $config `
                -Context $context `
                -TemplateFile $TemplateFile `
                -Method 'Unavailable' `
                -Risk $risk `
                -Status 'Warning' `
                -ErrorCode 'AzureSubscriptionUnavailable' `
                -ErrorMessage 'Azure subscription context is required before running what-if.'
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-whatif.json' `
                -InputObject $artifact
        }

        New-PM365Result `
            -Status 'Warning' `
            -Code 'AzureSubscriptionUnavailable' `
            -Summary 'Azure subscription context is required before running what-if.' `
            -Details 'Run Set-AzContext with the target subscription before deployment preview.' `
            -RetrySafe $true `
            -Data (New-PM365WhatIfResultData -Risk $risk -ArtifactPath $artifactPath)
        return
    }

    $expectedSubscriptionId = [string]$config.azure.subscriptionId
    $actualSubscriptionId = [string]$context.Subscription.Id
    if (-not [string]::IsNullOrWhiteSpace($expectedSubscriptionId) -and $expectedSubscriptionId -ne $actualSubscriptionId) {
        $risk = Get-PM365WhatIfRisk -UnstructuredFallback
        $artifactPath = ''
        $details = "Current Azure subscription '$actualSubscriptionId' does not match package subscription '$expectedSubscriptionId'. Select the package subscription before running sandbox what-if."
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365WhatIfArtifact `
                -Config $config `
                -Context $context `
                -TemplateFile $TemplateFile `
                -Method 'SubscriptionPreflight' `
                -Risk $risk `
                -Status 'Failed' `
                -ErrorCode 'AzureSubscriptionMismatch' `
                -ErrorMessage $details
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-whatif.json' `
                -InputObject $artifact
        }

        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $details = "$details$([Environment]::NewLine)Artifact: $artifactPath"
        }

        New-PM365Result `
            -Status 'Failed' `
            -Code 'AzureSubscriptionMismatch' `
            -Summary 'Azure subscription context does not match the customer package.' `
            -Details $details `
            -RetrySafe $true `
            -Data (New-PM365WhatIfResultData -Risk $risk -ArtifactPath $artifactPath)
        return
    }

    $upgradeContract = Test-PM365UpgradeContract -ConfigPath $ConfigPath -AllowTargetStateResume:$AllowUpgradeTargetStateResume
    if ($upgradeContract.status -eq 'Failed') {
        return $upgradeContract
    }

    $resourceGroupName = [string]$config.azure.resourceGroupName
    $resourceGroup = Get-AzResourceGroup -Name $resourceGroupName -ErrorAction SilentlyContinue
    if ($resourceGroup) {
        $productTag = [string](Get-PM365ObjectProperty -InputObject $resourceGroup.Tags -Name @('product'))
        $managedByTag = [string](Get-PM365ObjectProperty -InputObject $resourceGroup.Tags -Name @('managedBy'))
    }
    if ($resourceGroup -and ($productTag -ne 'PageMaker365' -or $managedByTag -ne 'PageMaker365')) {
        $risk = Get-PM365WhatIfRisk -UnstructuredFallback
        $artifactPath = ''
        $details = "Resource group '$resourceGroupName' exists but does not have the required product=PageMaker365 and managedBy=PageMaker365 ownership tags."
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365WhatIfArtifact `
                -Config $config `
                -Context $context `
                -TemplateFile $TemplateFile `
                -Method 'ResourceGroupOwnershipPreflight' `
                -Risk $risk `
                -Status 'Failed' `
                -ErrorCode 'AzureResourceGroupOwnershipMismatch' `
                -ErrorMessage $details
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-whatif.json' `
                -InputObject $artifact
        }

        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $details = "$details$([Environment]::NewLine)Artifact: $artifactPath"
        }

        New-PM365Result `
            -Status 'Failed' `
            -Code 'AzureResourceGroupOwnershipMismatch' `
            -Summary 'The existing target resource group is not owned by PageMaker365.' `
            -Details $details `
            -RetrySafe $false `
            -Data (New-PM365WhatIfResultData -Risk $risk -ArtifactPath $artifactPath)
        return
    }

    $parameters = New-PM365TemplateParameterObject -Config $config
    $deploymentArguments = @{
        Location = [string]$config.azure.location
        TemplateFile = $TemplateFile
        TemplateParameterObject = $parameters
        ErrorAction = 'Stop'
    }

    $structuredWhatIfCommand = Get-Command -Name Get-AzSubscriptionDeploymentWhatIfResult -ErrorAction SilentlyContinue

    try {
        if ($structuredWhatIfCommand) {
            $method = 'Get-AzSubscriptionDeploymentWhatIfResult'
            # Full payloads preserve Create, Modify, and NoChange instead of collapsing
            # every evaluated resource into the ambiguous ResourceIdOnly "Deploy" type.
            $structuredDeploymentArguments = $deploymentArguments.Clone()
            $structuredDeploymentArguments.ResultFormat = 'FullResourcePayloads'
            $structuredDeploymentArguments.SkipTemplateParameterPrompt = $true
            $whatIf = Get-AzSubscriptionDeploymentWhatIfResult @structuredDeploymentArguments
            $changes = @(Get-PM365WhatIfChanges -WhatIfResult $whatIf)
            $risk = Get-PM365WhatIfRisk -Changes $changes
            $resultMetadata = [ordered]@{
                status = [string](Get-PM365ObjectProperty -InputObject $whatIf -Name @('Status', 'status'))
                changeCount = $changes.Count
            }

            $artifactPath = ''
            if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
                $artifact = New-PM365WhatIfArtifact `
                    -Config $config `
                    -Context $context `
                    -TemplateFile $TemplateFile `
                    -Method $method `
                    -Risk $risk `
                    -Status ([string]$risk.status) `
                    -Changes $changes `
                    -ResultMetadata $resultMetadata
                $artifactPath = Write-PM365JsonArtifact `
                    -OutputPath $OutputPath `
                    -DefaultFileName 'deployment-whatif.json' `
                    -InputObject $artifact
            }

            $details = 'Create: {0}; Modify: {1}; Deploy: {2}; Delete: {3}; Ignore: {4}; Unknown: {5}; Blocked: {6}' -f `
                $risk.createCount,
                $risk.modifyCount,
                $risk.deployCount,
                $risk.deleteCount,
                $risk.ignoreCount,
                $risk.unknownCount,
                $risk.blockedCount
            if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
                $details = "$details; Artifact: $artifactPath"
            }

            if ($risk.blockedCount -gt 0) {
                New-PM365Result `
                    -Status 'Failed' `
                    -Code 'AzureWhatIfFailed' `
                    -Summary 'Azure deployment preview contains blocked changes.' `
                    -Details $details `
                    -RetrySafe $true `
                    -Data (New-PM365WhatIfResultData -Risk $risk -ArtifactPath $artifactPath)
                return
            }

            if ($risk.warningCount -gt 0) {
                New-PM365Result `
                    -Status 'Warning' `
                    -Code 'AzureWhatIfReady' `
                    -Summary 'Azure deployment preview completed with risk warnings.' `
                    -Details $details `
                    -RetrySafe $true `
                    -Data (New-PM365WhatIfResultData -Risk $risk -ArtifactPath $artifactPath)
                return
            }

            New-PM365Result `
                -Status 'Passed' `
                -Code 'AzureWhatIfReady' `
                -Summary 'Azure deployment preview completed.' `
                -Details $details `
                -Data (New-PM365WhatIfResultData -Risk $risk -ArtifactPath $artifactPath)
            return
        }

        Invoke-PM365UnstructuredWhatIf `
            -Config $config `
            -Context $context `
            -TemplateFile $TemplateFile `
            -DeploymentArguments $deploymentArguments `
            -OutputPath $OutputPath `
            -ReasonCode 'StructuredWhatIfUnavailable' `
            -ReasonMessage 'Get-AzSubscriptionDeploymentWhatIfResult is unavailable; unstructured what-if output was captured.'
    } catch {
        if ($structuredWhatIfCommand) {
            $structuredErrorMessage = $_.Exception.Message
            return Invoke-PM365UnstructuredWhatIf `
                -Config $config `
                -Context $context `
                -TemplateFile $TemplateFile `
                -DeploymentArguments $deploymentArguments `
                -OutputPath $OutputPath `
                -ReasonCode 'StructuredWhatIfFailedFallbackUsed' `
                -ReasonMessage "Get-AzSubscriptionDeploymentWhatIfResult failed, so unstructured what-if output was captured. Structured error: $structuredErrorMessage"
        }

        $risk = Get-PM365WhatIfRisk -UnstructuredFallback
        $artifactPath = ''
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365WhatIfArtifact `
                -Config $config `
                -Context $context `
                -TemplateFile $TemplateFile `
                -Method $method `
                -Risk $risk `
                -Status 'Failed' `
                -ErrorCode 'AzureWhatIfFailed' `
                -ErrorMessage $_.Exception.Message
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-whatif.json' `
                -InputObject $artifact
        }

        $details = $_.Exception.Message
        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $details = "$details$([Environment]::NewLine)Artifact: $artifactPath"
        }

        New-PM365Result `
            -Status 'Failed' `
            -Code 'AzureWhatIfFailed' `
            -Summary 'Azure deployment preview failed.' `
            -Details $details `
            -RetrySafe $true `
            -Data (New-PM365WhatIfResultData -Risk $risk -ArtifactPath $artifactPath)
    }
}
