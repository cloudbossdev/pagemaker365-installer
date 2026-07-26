function Get-PM365PartialInstallInventoryData {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Config
    )

    $context = Get-AzContext -ErrorAction SilentlyContinue
    $resourceGroupName = [string]$Config.azure.resourceGroupName
    $expectedSubscriptionId = [string]$Config.azure.subscriptionId
    $expectedTenantId = [string]$Config.azure.tenantId
    $expectedAppName = [string]$Config.app.appName
    $blockers = @()

    if (-not $context -or -not $context.Subscription.Id) {
        $blockers += 'An Azure subscription context is required.'
    } elseif (-not [string]::IsNullOrWhiteSpace($expectedSubscriptionId) -and $context.Subscription.Id -ne $expectedSubscriptionId) {
        $blockers += "Current subscription '$($context.Subscription.Id)' does not match package subscription '$expectedSubscriptionId'."
    }
    if ($context -and -not [string]::IsNullOrWhiteSpace($expectedTenantId) -and $context.Tenant.Id -ne $expectedTenantId) {
        $blockers += "Current tenant '$($context.Tenant.Id)' does not match package tenant '$expectedTenantId'."
    }

    $resourceGroup = Get-AzResourceGroup -Name $resourceGroupName -ErrorAction SilentlyContinue
    $resources = @()
    $deployments = @()
    $groupProduct = ''
    $groupManagedBy = ''
    if ($resourceGroup) {
        $groupProduct = [string](Get-PM365ObjectProperty -InputObject $resourceGroup.Tags -Name @('product'))
        $groupManagedBy = [string](Get-PM365ObjectProperty -InputObject $resourceGroup.Tags -Name @('managedBy'))
        if ($groupProduct -ne 'PageMaker365' -or $groupManagedBy -ne 'PageMaker365') {
            $blockers += 'The target resource group does not have the required PageMaker365 ownership tags.'
        }

        $resources = @(
            Get-AzResource -ResourceGroupName $resourceGroupName -ExpandProperties -ErrorAction Stop |
                Sort-Object ResourceType, Name |
                ForEach-Object {
                    $productTag = [string](Get-PM365ObjectProperty -InputObject $_.Tags -Name @('product'))
                    $appNameTag = [string](Get-PM365ObjectProperty -InputObject $_.Tags -Name @('appName'))
                    $owned = $productTag -eq 'PageMaker365' -and $appNameTag -eq $expectedAppName
                    if (-not $owned) {
                        $blockers += "Resource '$($_.ResourceId)' does not match the package ownership tags."
                    }

                    [pscustomobject][ordered]@{
                        name = [string]$_.Name
                        resourceType = [string]$_.ResourceType
                        location = [string]$_.Location
                        resourceId = [string]$_.ResourceId
                        ownedByPackage = $owned
                        ownershipTags = [ordered]@{
                            product = $productTag
                            appName = $appNameTag
                        }
                    }
                }
        )
        $deployments = @(
            Get-AzResourceGroupDeployment -ResourceGroupName $resourceGroupName -ErrorAction SilentlyContinue |
                Sort-Object Timestamp -Descending |
                ForEach-Object {
                    [pscustomobject][ordered]@{
                        name = [string]$_.DeploymentName
                        provisioningState = [string]$_.ProvisioningState
                        timestamp = $_.Timestamp
                        correlationId = [string]$_.CorrelationId
                    }
                }
        )
        $activeDeployments = @($deployments | Where-Object provisioningState -in @('Running', 'Accepted'))
        if ($activeDeployments.Count -gt 0) {
            $blockers += "$($activeDeployments.Count) Azure deployment(s) are still active."
        }
    }

    [pscustomobject][ordered]@{
        artifactType = 'PageMaker365.PartialInstallCleanupInventory'
        schemaVersion = '0.1'
        generatedAt = (Get-Date).ToUniversalTime().ToString('o')
        tenantId = if ($context) { [string]$context.Tenant.Id } else { '' }
        subscriptionId = if ($context) { [string]$context.Subscription.Id } else { '' }
        expectedSubscriptionId = $expectedSubscriptionId
        expectedTenantId = $expectedTenantId
        resourceGroupName = $resourceGroupName
        resourceGroupFound = $null -ne $resourceGroup
        resourceGroupLocation = if ($resourceGroup) { [string]$resourceGroup.Location } else { '' }
        resourceGroupOwnershipTags = [ordered]@{
            product = $groupProduct
            managedBy = $groupManagedBy
        }
        expectedAppName = $expectedAppName
        resources = $resources
        resourceCount = $resources.Count
        deployments = $deployments
        deploymentCount = $deployments.Count
        blockers = $blockers
        safeToRemove = $null -ne $resourceGroup -and $blockers.Count -eq 0
        cleanupAction = 'DeleteDedicatedResourceGroup'
        keyVault = [pscustomobject][ordered]@{
            name = [string]$Config.azure.resourceNames.keyVaultName
            disposition = 'RetainSoftDeletedRecoverable'
            purgeAllowed = $false
        }
    }
}
