function Get-PM365AzureDiscovery {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    $findings = @()
    $accessibleSubscriptions = @()
    $expectedTenantId = [string]$config.customer.tenantId
    if ([string]::IsNullOrWhiteSpace($expectedTenantId)) {
        $expectedTenantId = [string]$config.azure.tenantId
    }

    $expectedSubscriptionId = [string]$config.azure.subscriptionId
    $targetResourceGroupName = [string]$config.azure.resourceGroupName
    $recommendedLocation = [string]$config.azure.location

    $result = [ordered]@{
        contractVersion = '0.1'
        source = 'AzurePowerShell'
        dataPolicy = 'InstallReadinessOnly'
        discoveredAt = (Get-Date).ToUniversalTime().ToString('o')
        accountId = ''
        tenantId = ''
        selectedSubscriptionId = ''
        selectedSubscriptionName = ''
        selectedSubscriptionState = ''
        recommendedLocation = $recommendedLocation
        targetResourceGroupName = $targetResourceGroupName
        resourceGroupExists = $false
        accessibleSubscriptions = @()
        findings = @()
    }

    $azAccounts = Get-Module -ListAvailable -Name Az.Accounts | Select-Object -First 1
    if (-not $azAccounts) {
        $findings += [ordered]@{
            severity = 'Warning'
            code = 'AzAccountsMissing'
            summary = 'Az.Accounts is required for Azure discovery.'
            details = 'Install the Az.Accounts module, then sign in and rerun discovery.'
        }

        $result.tenantId = $expectedTenantId
        $result.selectedSubscriptionId = $expectedSubscriptionId
        $result.selectedSubscriptionName = if ($expectedSubscriptionId) { "Subscription $($expectedSubscriptionId.Substring(0, [Math]::Min(8, $expectedSubscriptionId.Length)))" } else { '' }
        $result.accessibleSubscriptions = if ($expectedSubscriptionId) {
            @([ordered]@{
                subscriptionId = $expectedSubscriptionId
                displayName = $result.selectedSubscriptionName
                state = 'Unknown'
            })
        } else {
            @()
        }
        $result.findings = $findings
        return [pscustomobject]$result
    }

    Import-Module Az.Accounts -ErrorAction Stop

    $context = Get-AzContext -ErrorAction SilentlyContinue
    if (-not $context) {
        $findings += [ordered]@{
            severity = 'Warning'
            code = 'AzureNotSignedIn'
            summary = 'Azure sign-in is required for live discovery.'
            details = 'Sign in to Azure before running tenant and subscription discovery.'
        }

        $result.tenantId = $expectedTenantId
        $result.selectedSubscriptionId = $expectedSubscriptionId
        $result.selectedSubscriptionName = if ($expectedSubscriptionId) { "Subscription $($expectedSubscriptionId.Substring(0, [Math]::Min(8, $expectedSubscriptionId.Length)))" } else { '' }
        $result.accessibleSubscriptions = if ($expectedSubscriptionId) {
            @([ordered]@{
                subscriptionId = $expectedSubscriptionId
                displayName = $result.selectedSubscriptionName
                state = 'Unknown'
            })
        } else {
            @()
        }
        $result.findings = $findings
        return [pscustomobject]$result
    }

    $result.accountId = [string]$context.Account.Id
    $result.tenantId = [string]$context.Tenant.Id
    $result.selectedSubscriptionId = [string]$context.Subscription.Id
    $result.selectedSubscriptionName = [string]$context.Subscription.Name
    $result.selectedSubscriptionState = [string]$context.Subscription.State

    if ($expectedTenantId -and $result.tenantId -and ($expectedTenantId -ne $result.tenantId)) {
        $findings += [ordered]@{
            severity = 'Failed'
            code = 'AzureTenantMismatch'
            summary = 'The signed-in Azure tenant does not match the customer package.'
            details = "Expected tenant $expectedTenantId but current context is $($result.tenantId)."
        }
    }

    if ($expectedSubscriptionId -and $result.selectedSubscriptionId -and ($expectedSubscriptionId -ne $result.selectedSubscriptionId)) {
        $findings += [ordered]@{
            severity = 'Failed'
            code = 'AzureSubscriptionMismatch'
            summary = 'The selected Azure subscription does not match the customer package.'
            details = "Expected subscription $expectedSubscriptionId but current context is $($result.selectedSubscriptionId)."
        }
    }

    try {
        $subscriptions = @(Get-AzSubscription -TenantId $result.tenantId -ErrorAction Stop)
        $accessibleSubscriptions = @($subscriptions | ForEach-Object {
            [ordered]@{
                subscriptionId = [string]$_.Id
                displayName = [string]$_.Name
                state = [string]$_.State
            }
        })
    } catch {
        $findings += [ordered]@{
            severity = 'Warning'
            code = 'AzureSubscriptionListUnavailable'
            summary = 'Accessible Azure subscriptions could not be listed.'
            details = $_.Exception.Message
        }
    }

    if ($accessibleSubscriptions.Count -eq 0 -and $result.selectedSubscriptionId) {
        $accessibleSubscriptions = @([ordered]@{
            subscriptionId = $result.selectedSubscriptionId
            displayName = $result.selectedSubscriptionName
            state = if ($result.selectedSubscriptionState) { $result.selectedSubscriptionState } else { 'Unknown' }
        })
    }

    $azResources = Get-Module -ListAvailable -Name Az.Resources | Select-Object -First 1
    if (-not $azResources) {
        $findings += [ordered]@{
            severity = 'Warning'
            code = 'AzResourcesMissing'
            summary = 'Az.Resources is required for resource group discovery.'
            details = 'Install Az.Resources to check whether the target resource group already exists.'
        }
    } elseif ($targetResourceGroupName) {
        try {
            Import-Module Az.Resources -ErrorAction Stop
            $resourceGroup = Get-AzResourceGroup -Name $targetResourceGroupName -ErrorAction SilentlyContinue
            if ($resourceGroup) {
                $result.resourceGroupExists = $true
                if ([string]::IsNullOrWhiteSpace($result.recommendedLocation)) {
                    $result.recommendedLocation = [string]$resourceGroup.Location
                }
            } else {
                $findings += [ordered]@{
                    severity = 'Warning'
                    code = 'AzureResourceGroupMissing'
                    summary = 'Target resource group does not exist yet.'
                    details = 'The deployment step can create it if the signed-in account has permission.'
                }
            }
        } catch {
            $findings += [ordered]@{
                severity = 'Warning'
                code = 'AzureResourceGroupCheckUnavailable'
                summary = 'Target resource group could not be checked.'
                details = $_.Exception.Message
            }
        }
    }

    if ($findings.Count -eq 0) {
        $findings += [ordered]@{
            severity = 'Info'
            code = 'AzureDiscoveryReady'
            summary = 'Azure discovery completed.'
            details = 'Azure tenant, subscription, and resource group metadata were collected with read-only commands.'
        }
    }

    $result.accessibleSubscriptions = $accessibleSubscriptions
    $result.findings = $findings
    [pscustomobject]$result
}
