function Test-PM365UpgradeContract {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $operation = [string](Get-PM365ObjectProperty -InputObject $config.deployment -Name @('operation'))
    $resourceGroupName = [string]$config.azure.resourceGroupName
    $azResources = Get-Module -ListAvailable -Name Az.Resources | Select-Object -First 1
    if (-not $azResources) {
        return New-PM365Result `
            -Status 'Failed' `
            -Code 'UpgradeContractCheckUnavailable' `
            -Summary 'Az.Resources is required to reconcile install or upgrade intent.' `
            -Details 'Install Az.Resources before previewing or deploying PageMaker365.' `
            -RetrySafe $true
    }

    Import-Module Az.Resources -ErrorAction Stop
    $context = Get-AzContext -ErrorAction SilentlyContinue
    if (-not $context) {
        return New-PM365Result `
            -Status 'Warning' `
            -Code 'UpgradeContractAzureContextUnavailable' `
            -Summary 'Azure sign-in is required to reconcile install or upgrade intent.' `
            -Details 'Sign in to the package subscription, then rerun preflight.' `
            -RetrySafe $true
    }

    $resourceGroup = Get-AzResourceGroup -Name $resourceGroupName -ErrorAction SilentlyContinue
    if ([string]::IsNullOrWhiteSpace($operation)) {
        if ($resourceGroup) {
            return New-PM365Result `
                -Status 'Failed' `
                -Code 'UpgradeIntentMissing' `
                -Summary 'The package does not identify an upgrade for the existing PageMaker365 environment.' `
                -Details 'Request a signed upgrade package that declares the source runtime version and deployment export.' `
                -RetrySafe $false
        }

        return New-PM365Result `
            -Status 'Warning' `
            -Code 'LegacyCleanInstallIntent' `
            -Summary 'The legacy package can proceed only as a clean install.' `
            -Details 'Production packages must declare deployment.operation and target runtime identity.' `
            -RetrySafe $false
    }

    $operation = $operation.Trim().ToLowerInvariant()
    if ($operation -notin @('install', 'upgrade')) {
        return New-PM365Result `
            -Status 'Failed' `
            -Code 'DeploymentOperationUnsupported' `
            -Summary 'The package deployment operation is not supported.' `
            -Details "Expected install or upgrade; package declares '$operation'." `
            -RetrySafe $false
    }

    $contractErrors = @()
    $stableVersionPattern = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'
    $targetVersionText = [string]$config.deployment.targetRuntimeVersion
    $minimumInstallerText = [string]$config.deployment.minimumInstallerVersion
    if ($targetVersionText -notmatch $stableVersionPattern) {
        $contractErrors += 'targetRuntimeVersion must use stable major.minor.patch format'
    }
    if ($minimumInstallerText -notmatch $stableVersionPattern) {
        $contractErrors += 'minimumInstallerVersion must use stable major.minor.patch format'
    } else {
        $moduleVersion = [version]'0.1.0'
        $loadedModule = Get-Module -Name PageMaker365.Install | Select-Object -First 1
        if ($loadedModule -and $loadedModule.Version) {
            $moduleVersion = [version]$loadedModule.Version
        }
        if ([version]$minimumInstallerText -gt $moduleVersion) {
            $contractErrors += "package requires installer $minimumInstallerText or later; current module is $moduleVersion"
        }
    }

    if ([string]$config.deployment.failureRecovery -ne 'ForwardFix') {
        $contractErrors += 'failureRecovery must be ForwardFix'
    }
    if ([string]$config.deployment.resourceNamePolicy -ne 'Immutable') {
        $contractErrors += 'resourceNamePolicy must be Immutable'
    }
    if ([string]$config.deployment.sharePointDataPolicy -ne 'Preserve') {
        $contractErrors += 'sharePointDataPolicy must be Preserve'
    }

    if ($operation -eq 'install') {
        if (-not [string]::IsNullOrWhiteSpace([string]$config.deployment.sourceRuntimeVersion) -or
            -not [string]::IsNullOrWhiteSpace([string]$config.deployment.sourceDeploymentExportId)) {
            $contractErrors += 'clean-install packages must not declare source runtime or export identity'
        }
    } else {
        $sourceVersionText = [string]$config.deployment.sourceRuntimeVersion
        if ($sourceVersionText -notmatch $stableVersionPattern) {
            $contractErrors += 'sourceRuntimeVersion must use stable major.minor.patch format'
        }
        if ([string]::IsNullOrWhiteSpace([string]$config.deployment.sourceDeploymentExportId)) {
            $contractErrors += 'sourceDeploymentExportId is required for upgrade'
        }
        if ($sourceVersionText -match $stableVersionPattern -and $targetVersionText -match $stableVersionPattern) {
            $sourceVersion = [version]$sourceVersionText
            $targetVersion = [version]$targetVersionText
            if ($targetVersion -le $sourceVersion) {
                $contractErrors += 'target runtime version must be greater than source runtime version'
            } elseif ($targetVersion.Major -ne $sourceVersion.Major) {
                $contractErrors += 'major-version upgrades are not supported'
            } elseif ($targetVersion.Minor -gt ($sourceVersion.Minor + 1)) {
                $contractErrors += 'upgrade cannot skip a minor runtime version'
            }
        }
    }

    if ($contractErrors.Count -gt 0) {
        return New-PM365Result `
            -Status 'Failed' `
            -Code 'UpgradePackageContractInvalid' `
            -Summary 'The package install or upgrade version contract is invalid.' `
            -Details ($contractErrors -join '; ') `
            -RetrySafe $false
    }

    if ($operation -eq 'install') {
        if ($resourceGroup) {
            $expectedResumeIdentity = @{
                product = 'PageMaker365'
                managedBy = 'PageMaker365'
                appName = [string]$config.app.appName
                installationId = [string]$config.customer.installationId
                runtimeVersion = [string]$config.deployment.targetRuntimeVersion
                deploymentExportId = [string]$config.controlPlane.deploymentExportId
                resourceNamesHash = Get-PM365ResourceNamesHash -ResourceNames $config.azure.resourceNames
            }
            $resumeMismatches = @()
            foreach ($name in $expectedResumeIdentity.Keys) {
                $actual = [string](Get-PM365ObjectProperty -InputObject $resourceGroup.Tags -Name @($name))
                if ([string]::IsNullOrWhiteSpace($expectedResumeIdentity[$name]) -or
                    -not [string]::Equals($actual, $expectedResumeIdentity[$name], [System.StringComparison]::OrdinalIgnoreCase)) {
                    $resumeMismatches += $name
                }
            }

            if ($resumeMismatches.Count -eq 0) {
                $activeInstallDeployments = @(Get-AzResourceGroupDeployment -ResourceGroupName $resourceGroupName -ErrorAction SilentlyContinue |
                    Where-Object { @('Accepted', 'Running') -contains [string]$_.ProvisioningState })
                if ($activeInstallDeployments.Count -gt 0) {
                    return New-PM365Result `
                        -Status 'Failed' `
                        -Code 'CleanInstallDeploymentInProgress' `
                        -Summary 'The matching PageMaker365 deployment is still active in Azure.' `
                        -Details 'Wait for the deployment to reach a terminal state, then resume the same saved installer session.' `
                        -RetrySafe $true
                }

                return New-PM365Result `
                    -Status 'Passed' `
                    -Code 'CleanInstallResumeReady' `
                    -Summary 'The existing deployment matches this clean-install package exactly.' `
                    -Details 'The same immutable package can reconcile its matching deployment without adopting another environment.' `
                    -Data @{
                        operation = 'install'
                        targetRuntimeVersion = [string]$config.deployment.targetRuntimeVersion
                        resume = $true
                    }
            }

            return New-PM365Result `
                -Status 'Failed' `
                -Code 'ExistingInstallationRequiresUpgradePackage' `
                -Summary 'A clean-install package cannot adopt or mutate a different existing PageMaker365 resource group.' `
                -Details ("Request an upgrade package or clean up the partial environment. Mismatched or missing Azure identity tags: " + ($resumeMismatches -join ', ') + '.') `
                -RetrySafe $false `
                -Data @{ operation = 'install'; mismatchedFields = ($resumeMismatches -join ',') }
        }

        return New-PM365Result `
            -Status 'Passed' `
            -Code 'CleanInstallIntentReady' `
            -Summary 'The clean-install package targets an absent resource group.' `
            -Details "Deployment will create '$resourceGroupName'." `
            -Data @{ operation = 'install'; targetRuntimeVersion = [string]$config.deployment.targetRuntimeVersion }
    }

    if (-not $resourceGroup) {
        return New-PM365Result `
            -Status 'Failed' `
            -Code 'UpgradeSourceInstallationMissing' `
            -Summary 'The upgrade source environment does not exist.' `
            -Details "Resource group '$resourceGroupName' was not found. Use a clean-install package or correct the target environment." `
            -RetrySafe $false
    }

    $expected = @{
        product = 'PageMaker365'
        managedBy = 'PageMaker365'
        appName = [string]$config.app.appName
        installationId = [string]$config.customer.installationId
        runtimeVersion = [string]$config.deployment.sourceRuntimeVersion
        deploymentExportId = [string]$config.deployment.sourceDeploymentExportId
        resourceNamesHash = Get-PM365ResourceNamesHash -ResourceNames $config.azure.resourceNames
    }
    $mismatches = @()
    foreach ($name in $expected.Keys) {
        $actual = [string](Get-PM365ObjectProperty -InputObject $resourceGroup.Tags -Name @($name))
        if ([string]::IsNullOrWhiteSpace($expected[$name]) -or
            -not [string]::Equals($actual, $expected[$name], [System.StringComparison]::OrdinalIgnoreCase)) {
            $mismatches += $name
        }
    }

    if ($mismatches.Count -gt 0) {
        return New-PM365Result `
            -Status 'Failed' `
            -Code 'UpgradeSourceIdentityMismatch' `
            -Summary 'The upgrade package does not match the existing PageMaker365 environment.' `
            -Details ("Request a new upgrade package. Mismatched or missing Azure identity tags: " + ($mismatches -join ', ') + '.') `
            -RetrySafe $false `
            -Data @{ operation = 'upgrade'; mismatchedFields = ($mismatches -join ',') }
    }

    $activeStates = @('Accepted', 'Running')
    $activeDeployments = @(Get-AzResourceGroupDeployment -ResourceGroupName $resourceGroupName -ErrorAction SilentlyContinue |
        Where-Object { $activeStates -contains [string]$_.ProvisioningState })
    if ($activeDeployments.Count -gt 0) {
        return New-PM365Result `
            -Status 'Failed' `
            -Code 'UpgradeDeploymentInProgress' `
            -Summary 'Another Azure deployment is active in the PageMaker365 resource group.' `
            -Details 'Wait for the active deployment to reach a terminal state, then rerun preflight and preview.' `
            -RetrySafe $true
    }

    New-PM365Result `
        -Status 'Passed' `
        -Code 'UpgradeIntentReady' `
        -Summary 'The upgrade package matches the existing PageMaker365 runtime identity.' `
        -Details "Upgrade $([string]$config.deployment.sourceRuntimeVersion) to $([string]$config.deployment.targetRuntimeVersion) is ready for preview." `
        -Data @{
            operation = 'upgrade'
            sourceRuntimeVersion = [string]$config.deployment.sourceRuntimeVersion
            targetRuntimeVersion = [string]$config.deployment.targetRuntimeVersion
            sourceDeploymentExportId = [string]$config.deployment.sourceDeploymentExportId
        }
}
