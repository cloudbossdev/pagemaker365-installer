[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$moduleRoot = Join-Path $repoRoot 'modules\PageMaker365.Install'

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

Get-ChildItem -Path (Join-Path $moduleRoot 'Private') -Filter '*.ps1' -File |
    ForEach-Object { . $_.FullName }
. (Join-Path $moduleRoot 'Public\Test-PM365AzurePlatformReadiness.ps1')

$script:subscriptionId = '00000000-0000-0000-0000-000000000001'
$script:expectedSubscriptionId = $script:subscriptionId
$script:location = 'eastus2'
$script:providerStates = @{}
$script:skuPayload = @{
    resourceType = 'serverFarms'
    skus = @(
        @{
            name = 'B1'
            size = 'B1'
            tier = 'Basic'
            locations = @('East US 2')
        }
    )
}
$script:usagePayload = @{
    value = @(
        @{
            name = @{ value = 'Cores usage in East US 2'; localizedValue = 'Cores usage in East US 2' }
            currentValue = 1
            limit = 10
            unit = 'Core Count'
        }
    )
}
$script:throwOnRest = $false
$script:restCallCount = 0

function Get-PM365Config {
    param([string] $ConfigPath)
    [pscustomobject]@{
        azure = [pscustomobject]@{
            subscriptionId = $script:expectedSubscriptionId
            location = $script:location
        }
    }
}

function Get-AzContext {
    param([System.Management.Automation.ActionPreference] $ErrorAction)
    [pscustomobject]@{
        Subscription = [pscustomobject]@{ Id = $script:subscriptionId }
    }
}

function Get-AzResourceProvider {
    param(
        [string[]] $ProviderNamespace,
        [System.Management.Automation.ActionPreference] $ErrorAction
    )

    foreach ($provider in $ProviderNamespace) {
        $state = if ($script:providerStates.ContainsKey($provider)) {
            [string]$script:providerStates[$provider]
        } else {
            'Registered'
        }

        [pscustomobject]@{
            ProviderNamespace = $provider
            RegistrationState = $state
        }
    }
}

function Invoke-AzRestMethod {
    param(
        [string] $Method,
        [string] $Path,
        [System.Management.Automation.ActionPreference] $ErrorAction
    )

    $script:restCallCount++
    if ($script:throwOnRest) {
        throw 'Simulated Azure Resource Manager timeout.'
    }

    if ($Path -match '/skus\?') {
        return [pscustomobject]@{
            StatusCode = 200
            Content = ($script:skuPayload | ConvertTo-Json -Depth 8 -Compress)
        }
    }

    if ($Path -match '/usages\?') {
        return [pscustomobject]@{
            StatusCode = 200
            Content = ($script:usagePayload | ConvertTo-Json -Depth 8 -Compress)
        }
    }

    throw "Unexpected Azure REST path: $Path"
}

$ready = @(Test-PM365AzurePlatformReadiness -ConfigPath 'test.json')
Assert-True ($ready.code -contains 'AzureResourceProvidersReady') 'Registered resource providers did not pass.'
Assert-True ($ready.code -contains 'AppServiceSkuReady') 'Eligible B1 SKU did not pass.'
Assert-True ($ready.code -contains 'AppServiceQuotaReady') 'Available App Service quota did not pass.'
Assert-True (($ready | Where-Object code -eq 'AppServiceQuotaReady').data.capacityReserved -eq $false) 'Quota readiness must not claim capacity reservation.'

$script:providerStates['Microsoft.Web'] = 'NotRegistered'
$providerFailure = @(Test-PM365AzurePlatformReadiness -ConfigPath 'test.json')
Assert-True ($providerFailure.code -contains 'AzureResourceProvidersNotRegistered') 'Unregistered Microsoft.Web provider did not block preflight.'
Assert-True (($providerFailure | Where-Object code -eq 'AzureResourceProvidersNotRegistered').status -eq 'Failed') 'Unregistered provider was not a failed result.'
$script:providerStates.Clear()

$script:skuPayload.skus = @(@{ name = 'B2'; locations = @('East US 2') })
$skuFailure = @(Test-PM365AzurePlatformReadiness -ConfigPath 'test.json')
Assert-True ($skuFailure.code -contains 'AppServiceSkuUnavailable') 'Unavailable B1 SKU did not block preflight.'
$script:skuPayload.skus = @(@{ name = 'B1'; locations = @('East US 2') })

$script:usagePayload.value[0].currentValue = 10
$quotaFailure = @(Test-PM365AzurePlatformReadiness -ConfigPath 'test.json')
Assert-True ($quotaFailure.code -contains 'AppServiceQuotaExhausted') 'Exhausted App Service quota did not block preflight.'
Assert-True (($quotaFailure | Where-Object code -eq 'AppServiceQuotaExhausted').status -eq 'Failed') 'Exhausted quota was not a failed result.'
$script:usagePayload.value[0].currentValue = 1

$script:usagePayload.value[0].limit = -1
$unlimitedQuota = @(Test-PM365AzurePlatformReadiness -ConfigPath 'test.json')
Assert-True ($unlimitedQuota.code -contains 'AppServiceQuotaReady') 'Unlimited App Service quota was treated as exhausted.'
Assert-True (($unlimitedQuota | Where-Object code -eq 'AppServiceQuotaReady').data.unlimited -eq $true) 'Unlimited quota was not identified in sanitized result data.'
$script:usagePayload.value[0].limit = 10

$script:throwOnRest = $true
$unavailableSignals = @(Test-PM365AzurePlatformReadiness -ConfigPath 'test.json')
Assert-True ($unavailableSignals.code -contains 'AppServiceSkuCheckUnavailable') 'SKU API failure was not surfaced as a warning.'
Assert-True ($unavailableSignals.code -contains 'AppServiceQuotaCheckUnavailable') 'Quota API failure was not surfaced as a warning.'
$script:throwOnRest = $false

$script:subscriptionId = '00000000-0000-0000-0000-000000000002'
$script:restCallCount = 0
$wrongSubscription = @(Test-PM365AzurePlatformReadiness -ConfigPath 'test.json')
Assert-True ($wrongSubscription.code -contains 'AzurePlatformReadinessSkipped') 'Wrong subscription did not skip platform readiness.'
Assert-True ($script:restCallCount -eq 0) 'Platform APIs were called against the wrong subscription.'

Write-Host 'Azure platform readiness preflight tests passed.'
