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
        $results += New-PM365Result `
            -Status 'Passed' `
            -Code 'AzureResourceGroupReady' `
            -Summary 'Target resource group exists.' `
            -Details $resourceGroup.ResourceGroupName
    } else {
        $results += New-PM365Result `
            -Status 'Warning' `
            -Code 'AzureResourceGroupMissing' `
            -Summary 'Target resource group does not exist yet.' `
            -Details 'The deployment step can create it if the signed-in account has permission.' `
            -RetrySafe $true
    }

    $subscriptionReady = $actualSubscriptionId -and ((-not $expectedSubscriptionId) -or ($expectedSubscriptionId -eq $actualSubscriptionId))
    if ($subscriptionReady) {
        $targetScopes = @("/subscriptions/$actualSubscriptionId")
        if ($resourceGroup) {
            $targetScopes += "/subscriptions/$actualSubscriptionId/resourceGroups/$resourceGroupName"
        }

        try {
            $accountId = [string]$context.Account.Id
            $assignments = foreach ($scope in $targetScopes) {
                Get-AzRoleAssignment -Scope $scope -ErrorAction Stop |
                    Where-Object {
                        $_.SignInName -eq $accountId -or
                        $_.DisplayName -eq $accountId -or
                        $_.ObjectId -eq $accountId
                    }
            }

            $roleNames = @($assignments | Select-Object -ExpandProperty RoleDefinitionName -Unique)
            $deploymentRoles = @('Owner', 'Contributor')
            $matchingRoles = @($roleNames | Where-Object { $_ -in $deploymentRoles })

            if ($matchingRoles.Count -gt 0) {
                $results += New-PM365Result `
                    -Status 'Passed' `
                    -Code 'AzureRbacReady' `
                    -Summary 'Azure RBAC appears sufficient for deployment.' `
                    -Details ("Matched role assignments: " + ($matchingRoles -join ', ')) `
                    -Data @{
                        account = $accountId
                        roles = ($roleNames -join ', ')
                    }
            } elseif ($roleNames.Count -gt 0) {
                $results += New-PM365Result `
                    -Status 'Warning' `
                    -Code 'AzureRbacInsufficient' `
                    -Summary 'Azure RBAC may not allow deployment.' `
                    -Details ("Current role assignments do not include Owner or Contributor: " + ($roleNames -join ', ')) `
                    -RetrySafe $true
            } else {
                $results += New-PM365Result `
                    -Status 'Warning' `
                    -Code 'AzureRbacNotFound' `
                    -Summary 'Azure RBAC role assignments were not found for the signed-in account.' `
                    -Details 'The account may inherit access through a group, or the installer may need an administrator to grant Contributor access.' `
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
