function Invoke-PM365WhatIf {
    [CmdletBinding()]
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

    $parameters = New-PM365TemplateParameterObject -Config $config
    $deploymentArguments = @{
        ResourceGroupName = [string]$config.azure.resourceGroupName
        TemplateFile = $TemplateFile
        TemplateParameterObject = $parameters
        ErrorAction = 'Stop'
    }

    $structuredWhatIfCommand = Get-Command -Name Get-AzResourceGroupDeploymentWhatIfResult -ErrorAction SilentlyContinue

    try {
        if ($structuredWhatIfCommand) {
            $method = 'Get-AzResourceGroupDeploymentWhatIfResult'
            $whatIf = Get-AzResourceGroupDeploymentWhatIfResult @deploymentArguments
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

            $details = 'Create: {0}; Modify: {1}; Delete: {2}; Ignore: {3}; Unknown: {4}; Blocked: {5}' -f `
                $risk.createCount,
                $risk.modifyCount,
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

        $method = 'New-AzResourceGroupDeployment -WhatIf'
        $whatIf = New-AzResourceGroupDeployment @deploymentArguments -WhatIf 2>&1
        $outputText = @($whatIf | ForEach-Object { [string]$_ })
        $risk = Get-PM365WhatIfRisk -UnstructuredFallback
        $artifactPath = ''
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $artifact = New-PM365WhatIfArtifact `
                -Config $config `
                -Context $context `
                -TemplateFile $TemplateFile `
                -Method $method `
                -Risk $risk `
                -Status 'Warning' `
                -Output $outputText `
                -ErrorCode 'StructuredWhatIfUnavailable' `
                -ErrorMessage 'Get-AzResourceGroupDeploymentWhatIfResult is unavailable; unstructured what-if output was captured.'
            $artifactPath = Write-PM365JsonArtifact `
                -OutputPath $OutputPath `
                -DefaultFileName 'deployment-whatif.json' `
                -InputObject $artifact
        }

        $details = ($outputText -join [Environment]::NewLine)
        if ([string]::IsNullOrWhiteSpace($details)) {
            $details = 'Get-AzResourceGroupDeploymentWhatIfResult is unavailable; unstructured what-if completed without captured text.'
        }
        if (-not [string]::IsNullOrWhiteSpace($artifactPath)) {
            $details = "$details$([Environment]::NewLine)Artifact: $artifactPath"
        }

        New-PM365Result `
            -Status 'Warning' `
            -Code 'AzureWhatIfReady' `
            -Summary 'Azure deployment preview completed without structured change data.' `
            -Details $details `
            -RetrySafe $true `
            -Data (New-PM365WhatIfResultData -Risk $risk -ArtifactPath $artifactPath)
    } catch {
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
