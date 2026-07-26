function Test-PM365AzureContext {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $results = @()
    $azAccounts = Get-Module -ListAvailable -Name Az.Accounts | Select-Object -First 1
    $azResources = Get-Module -ListAvailable -Name Az.Resources | Select-Object -First 1

    if (-not $azAccounts) {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'AzAccountsMissing' `
            -Summary 'Az.Accounts is required for Azure sign-in checks.' `
            -Details 'Install the Az.Accounts module before running Azure validation.' `
            -RetrySafe $true
        return $results
    }

    Import-Module Az.Accounts -ErrorAction Stop

    $context = Get-AzContext -ErrorAction SilentlyContinue
    if (-not $context) {
        $results += New-PM365Result `
            -Status 'Warning' `
            -Code 'AzureNotSignedIn' `
            -Summary 'Azure sign-in is required.' `
            -Details 'Sign in to Azure before running tenant and subscription validation.' `
            -RetrySafe $true
        return $results
    }

    $expectedTenantId = [string]$config.customer.tenantId
    $actualTenantId = [string]$context.Tenant.Id
    if ($expectedTenantId -and $actualTenantId -and ($expectedTenantId -ne $actualTenantId)) {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'AzureTenantMismatch' `
            -Summary 'The signed-in Azure tenant does not match the customer package.' `
            -Details "Expected tenant $expectedTenantId but current context is $actualTenantId." `
            -RetrySafe $true
    } else {
        $results += New-PM365Result `
            -Status 'Passed' `
            -Code 'AzureTenantReady' `
            -Summary 'Azure tenant context matches the customer package.' `
            -Details $actualTenantId
    }

    $expectedSubscriptionId = [string]$config.azure.subscriptionId
    $actualSubscriptionId = [string]$context.Subscription.Id
    if ($expectedSubscriptionId -and -not $actualSubscriptionId) {
        $results += New-PM365Result `
            -Status 'Warning' `
            -Code 'AzureSubscriptionUnavailable' `
            -Summary 'Azure subscription context could not be read.' `
            -Details 'Run Set-AzContext with the target subscription before deployment validation.' `
            -RetrySafe $true
    } elseif ($expectedSubscriptionId -and $actualSubscriptionId -and ($expectedSubscriptionId -ne $actualSubscriptionId)) {
        $results += New-PM365Result `
            -Status 'Failed' `
            -Code 'AzureSubscriptionMismatch' `
            -Summary 'The selected Azure subscription does not match the customer package.' `
            -Details "Expected subscription $expectedSubscriptionId but current context is $actualSubscriptionId." `
            -RetrySafe $true
    } else {
        $results += New-PM365Result `
            -Status 'Passed' `
            -Code 'AzureSubscriptionReady' `
            -Summary 'Azure subscription context matches the customer package.' `
            -Details $actualSubscriptionId
    }

    if (-not $azResources) {
        $results += New-PM365Result `
            -Status 'Warning' `
            -Code 'AzResourcesMissing' `
            -Summary 'Az.Resources is not installed.' `
            -Details 'Resource group and deployment checks require Az.Resources.' `
            -RetrySafe $true
        return $results
    }

    Import-Module Az.Resources -ErrorAction Stop
    $resourceGroupName = [string]$config.azure.resourceGroupName
    $resourceGroup = Get-AzResourceGroup -Name $resourceGroupName -ErrorAction SilentlyContinue
    if ($resourceGroup) {
        $productTag = [string](Get-PM365ObjectProperty -InputObject $resourceGroup.Tags -Name @('product'))
        $managedByTag = [string](Get-PM365ObjectProperty -InputObject $resourceGroup.Tags -Name @('managedBy'))
        if ($productTag -eq 'PageMaker365' -and $managedByTag -eq 'PageMaker365') {
            $results += New-PM365Result `
                -Status 'Passed' `
                -Code 'AzureResourceGroupReady' `
                -Summary 'Target resource group exists and is owned by PageMaker365.' `
                -Details $resourceGroup.ResourceGroupName
        } else {
            $results += New-PM365Result `
                -Status 'Failed' `
                -Code 'AzureResourceGroupOwnershipMismatch' `
                -Summary 'The existing target resource group is not owned by PageMaker365.' `
                -Details "Resource group '$resourceGroupName' must have product=PageMaker365 and managedBy=PageMaker365 tags before it can be reused." `
                -RetrySafe $false
        }
    } else {
        $results += New-PM365Result `
            -Status 'Passed' `
            -Code 'AzureResourceGroupWillBeCreated' `
            -Summary 'The dedicated PageMaker365 resource group will be created during deployment.' `
            -Details "Subscription-scope preview and deployment will create '$resourceGroupName' in $([string]$config.azure.location)."
    }

    $subscriptionReady = $actualSubscriptionId -and ((-not $expectedSubscriptionId) -or ($expectedSubscriptionId -eq $actualSubscriptionId))
    if ($subscriptionReady) {
        try {
            $accountId = [string]$context.Account.Id
            $subscriptionScope = "/subscriptions/$actualSubscriptionId"
            $roleCommand = Get-Command Get-AzRoleAssignment -ErrorAction Stop
            $roleArguments = @{
                Scope = $subscriptionScope
                ErrorAction = 'Stop'
            }
            if ($roleCommand.Parameters.ContainsKey('ExpandPrincipalGroups')) {
                $roleArguments.ExpandPrincipalGroups = $true
            }

            $parsedObjectId = [guid]::Empty
            if ([guid]::TryParse($accountId, [ref]$parsedObjectId) -and $roleCommand.Parameters.ContainsKey('ObjectId')) {
                $roleArguments.ObjectId = $accountId
            } elseif ($roleCommand.Parameters.ContainsKey('SignInName')) {
                $roleArguments.SignInName = $accountId
            }

            $assignments = @(Get-AzRoleAssignment @roleArguments)
            if (-not $roleArguments.ContainsKey('ObjectId') -and -not $roleArguments.ContainsKey('SignInName')) {
                $assignments = @($assignments | Where-Object {
                    $_.SignInName -eq $accountId -or
                    $_.DisplayName -eq $accountId -or
                    $_.ObjectId -eq $accountId
                })
            }

            $roleNames = @($assignments | Select-Object -ExpandProperty RoleDefinitionName -Unique)
            $roleCheck = Test-PM365AzureDeploymentRoleSet -RoleNames $roleNames

            if ($roleCheck.ready) {
                $results += New-PM365Result `
                    -Status 'Passed' `
                    -Code 'AzureRbacReady' `
                    -Summary 'Azure RBAC is sufficient for resource deployment and managed-identity role assignment.' `
                    -Details ("Effective subscription roles: " + ($roleNames -join ', ')) `
                    -Data @{
                        account = $accountId
                        roles = ($roleNames -join ', ')
                    }
            } elseif ($roleNames.Count -gt 0) {
                $results += New-PM365Result `
                    -Status 'Failed' `
                    -Code 'AzureRbacInsufficient' `
                    -Summary 'Azure RBAC cannot complete the PageMaker365 deployment.' `
                    -Details ("Deployment requires Owner, or Contributor plus Role Based Access Control Administrator or User Access Administrator, effective at the subscription scope. Current roles: " + ($roleNames -join ', ')) `
                    -RetrySafe $true
            } else {
                $results += New-PM365Result `
                    -Status 'Failed' `
                    -Code 'AzureRbacNotFound' `
                    -Summary 'Required Azure RBAC role assignments were not found for the signed-in account.' `
                    -Details 'Grant Owner, or Contributor plus Role Based Access Control Administrator or User Access Administrator, effective at the target subscription.' `
                    -RetrySafe $true
            }
        } catch {
            $results += New-PM365Result `
                -Status 'Warning' `
                -Code 'AzureRbacCheckUnavailable' `
                -Summary 'Azure RBAC could not be verified.' `
                -Details $_.Exception.Message `
                -RetrySafe $true
        }
    }

    $results
}
