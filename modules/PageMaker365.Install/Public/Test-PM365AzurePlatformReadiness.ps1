function Test-PM365AzurePlatformReadiness {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    if (-not (Get-Command Get-AzContext -ErrorAction SilentlyContinue)) {
        New-PM365Result `
            -Status 'Skipped' `
            -Code 'AzurePlatformReadinessSkipped' `
            -Summary 'Azure platform readiness was not checked.' `
            -Details 'Install Az.Accounts, sign in to Azure, and rerun Preflight.'
        return
    }

    $context = Get-AzContext -ErrorAction SilentlyContinue
    $expectedSubscriptionId = [string]$config.azure.subscriptionId
    $actualSubscriptionId = [string]$context.Subscription.Id
    $location = [string]$config.azure.location

    if (-not $context -or [string]::IsNullOrWhiteSpace($actualSubscriptionId)) {
        New-PM365Result `
            -Status 'Skipped' `
            -Code 'AzurePlatformReadinessSkipped' `
            -Summary 'Azure platform readiness was not checked.' `
            -Details 'Sign in to Azure and select the package subscription before checking provider, SKU, and quota readiness.'
        return
    }

    if ((-not [string]::IsNullOrWhiteSpace($expectedSubscriptionId)) -and $actualSubscriptionId -ne $expectedSubscriptionId) {
        New-PM365Result `
            -Status 'Skipped' `
            -Code 'AzurePlatformReadinessSkipped' `
            -Summary 'Azure platform readiness was not checked in the wrong subscription.' `
            -Details "Select package subscription '$expectedSubscriptionId' before running this check."
        return
    }

    if ([string]::IsNullOrWhiteSpace($location)) {
        New-PM365Result `
            -Status 'Failed' `
            -Code 'AzurePlatformReadinessContractMissing' `
            -Summary 'The package is missing the Azure deployment location.' `
            -Details 'Regenerate the signed package with a supported Azure location.' `
            -RetrySafe $false
        return
    }

    $requiredProviders = @(
        'Microsoft.Authorization',
        'Microsoft.Insights',
        'Microsoft.KeyVault',
        'Microsoft.ManagedIdentity',
        'Microsoft.OperationalInsights',
        'Microsoft.Storage',
        'Microsoft.Web'
    )

    try {
        $providers = @(Get-AzResourceProvider -ProviderNamespace $requiredProviders -ErrorAction Stop)
        $unregisteredProviders = @($requiredProviders | Where-Object {
            $providerNamespace = $_
            -not @($providers | Where-Object {
                [string]$_.ProviderNamespace -eq $providerNamespace -and
                [string]$_.RegistrationState -eq 'Registered'
            })
        })

        if ($unregisteredProviders.Count -gt 0) {
            New-PM365Result `
                -Status 'Failed' `
                -Code 'AzureResourceProvidersNotRegistered' `
                -Summary 'One or more Azure resource providers required by PageMaker365 are not registered.' `
                -Details ("Register these providers in the package subscription, then rerun Preflight: " + ($unregisteredProviders -join ', ')) `
                -RetrySafe $true `
                -Data @{
                    requiredProviders = ($requiredProviders -join ', ')
                    unregisteredProviders = ($unregisteredProviders -join ', ')
                }
        } else {
            New-PM365Result `
                -Status 'Passed' `
                -Code 'AzureResourceProvidersReady' `
                -Summary 'Required Azure resource providers are registered.' `
                -Details ($requiredProviders -join ', ') `
                -Data @{
                    requiredProviders = ($requiredProviders -join ', ')
                }
        }
    } catch {
        New-PM365Result `
            -Status 'Warning' `
            -Code 'AzureResourceProviderCheckUnavailable' `
            -Summary 'Azure resource-provider registration could not be verified.' `
            -Details $_.Exception.Message `
            -RetrySafe $true
    }

    $normalizedLocation = ($location -replace '[^A-Za-z0-9]', '').ToLowerInvariant()
    $escapedLocation = [Uri]::EscapeDataString($location)
    $skuPath = "/subscriptions/$actualSubscriptionId/providers/Microsoft.Web/skus?api-version=2025-05-01"

    try {
        $skuResponse = Invoke-AzRestMethod -Method GET -Path $skuPath -ErrorAction Stop
        if ($skuResponse.StatusCode -lt 200 -or $skuResponse.StatusCode -ge 300) {
            throw "Azure Resource Manager returned HTTP $($skuResponse.StatusCode) for App Service SKU readiness."
        }

        $skuPayload = $skuResponse.Content | ConvertFrom-Json
        $eligibleSku = @($skuPayload.skus | Where-Object {
            $sku = $_
            $skuName = [string](Get-PM365ObjectProperty -InputObject $sku -Name @('name', 'size'))
            $locations = @($sku.locations | ForEach-Object { ([string]$_ -replace '[^A-Za-z0-9]', '').ToLowerInvariant() })
            $skuName -eq 'B1' -and $locations -contains $normalizedLocation
        } | Select-Object -First 1)

        if ($eligibleSku.Count -eq 0) {
            New-PM365Result `
                -Status 'Failed' `
                -Code 'AppServiceSkuUnavailable' `
                -Summary 'The required App Service B1 SKU is not available to this subscription in the package region.' `
                -Details "Choose a supported package region or resolve the subscription SKU restriction for '$location', then rerun Preflight." `
                -RetrySafe $true `
                -Data @{
                    location = $location
                    sku = 'B1'
                }
        } else {
            New-PM365Result `
                -Status 'Passed' `
                -Code 'AppServiceSkuReady' `
                -Summary 'The App Service B1 SKU is available to this subscription in the package region.' `
                -Details "$location / B1" `
                -Data @{
                    location = $location
                    sku = 'B1'
                }
        }
    } catch {
        New-PM365Result `
            -Status 'Warning' `
            -Code 'AppServiceSkuCheckUnavailable' `
            -Summary 'App Service SKU readiness could not be verified.' `
            -Details $_.Exception.Message `
            -RetrySafe $true
    }

    $usagePath = "/subscriptions/$actualSubscriptionId/providers/Microsoft.Web/locations/$escapedLocation/usages?api-version=2025-05-01"
    try {
        $usageResponse = Invoke-AzRestMethod -Method GET -Path $usagePath -ErrorAction Stop
        if ($usageResponse.StatusCode -lt 200 -or $usageResponse.StatusCode -ge 300) {
            throw "Azure Resource Manager returned HTTP $($usageResponse.StatusCode) for App Service quota readiness."
        }

        $usagePayload = $usageResponse.Content | ConvertFrom-Json
        $coreQuotas = @($usagePayload.value | Where-Object {
            [string]$_.unit -eq 'Core Count' -or [string]$_.name.value -match '(?i)core'
        })

        if ($coreQuotas.Count -eq 0) {
            New-PM365Result `
                -Status 'Warning' `
                -Code 'AppServiceQuotaSignalUnavailable' `
                -Summary 'Azure did not return an App Service core-quota signal for the package region.' `
                -Details 'Preflight cannot reserve regional capacity. Continue only after reviewing the deployment preview; Azure may still reject allocation during install.' `
                -RetrySafe $true
        } else {
            $boundedQuotas = @($coreQuotas | Where-Object { [long]$_.limit -ge 0 })
            $exhaustedQuotas = @($boundedQuotas | Where-Object {
                ([long]$_.limit - [long]$_.currentValue) -lt 1
            })

            if ($exhaustedQuotas.Count -gt 0) {
                $quotaNames = @($exhaustedQuotas | ForEach-Object { [string]$_.name.value })
                New-PM365Result `
                    -Status 'Failed' `
                    -Code 'AppServiceQuotaExhausted' `
                    -Summary 'The subscription has no remaining App Service core quota in the package region.' `
                    -Details ("Request App Service quota or use a newly approved package region, then rerun Preflight. Exhausted signal(s): " + ($quotaNames -join ', ')) `
                    -RetrySafe $true `
                    -Data @{
                        location = $location
                        quotaSignals = ($quotaNames -join ', ')
                        requiredCores = 1
                    }
            } else {
                $minimumRemaining = if ($boundedQuotas.Count -gt 0) {
                    @($boundedQuotas | ForEach-Object { [long]$_.limit - [long]$_.currentValue } | Measure-Object -Minimum).Minimum
                } else {
                    -1
                }
                $quotaDetails = if ($minimumRemaining -lt 0) {
                    "Azure reports unlimited App Service core quota in '$location'. This does not reserve regional capacity."
                } else {
                    "At least $minimumRemaining App Service core(s) remain in '$location'. This does not reserve regional capacity."
                }
                New-PM365Result `
                    -Status 'Passed' `
                    -Code 'AppServiceQuotaReady' `
                    -Summary 'Azure reports available App Service core quota in the package region.' `
                    -Details $quotaDetails `
                    -Data @{
                        location = $location
                        minimumRemainingCores = $minimumRemaining
                        requiredCores = 1
                        unlimited = ($minimumRemaining -lt 0)
                        capacityReserved = $false
                    }
            }
        }
    } catch {
        New-PM365Result `
            -Status 'Warning' `
            -Code 'AppServiceQuotaCheckUnavailable' `
            -Summary 'App Service quota readiness could not be verified.' `
            -Details $_.Exception.Message `
            -RetrySafe $true
    }
}
