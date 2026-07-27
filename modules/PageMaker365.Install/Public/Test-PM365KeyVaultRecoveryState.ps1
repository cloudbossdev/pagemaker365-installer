function Test-PM365KeyVaultRecoveryState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ConfigPath
    )

    $config = Get-PM365Config -ConfigPath $ConfigPath
    if (-not (Get-Command Get-AzContext -ErrorAction SilentlyContinue)) {
        New-PM365Result `
            -Status 'Skipped' `
            -Code 'KeyVaultRecoveryCheckSkipped' `
            -Summary 'Key Vault recovery state was not checked.' `
            -Details 'Install Az.Accounts, sign in to Azure, and rerun Preflight.'
        return
    }

    $context = Get-AzContext -ErrorAction SilentlyContinue
    if (-not $context -or -not $context.Subscription.Id) {
        New-PM365Result `
            -Status 'Skipped' `
            -Code 'KeyVaultRecoveryCheckSkipped' `
            -Summary 'Key Vault recovery state was not checked.' `
            -Details 'Sign in to Azure and select the package subscription before checking retained vault names.'
        return
    }

    $expectedSubscriptionId = [string]$config.azure.subscriptionId
    $actualSubscriptionId = [string]$context.Subscription.Id
    if (-not [string]::IsNullOrWhiteSpace($expectedSubscriptionId) -and $actualSubscriptionId -ne $expectedSubscriptionId) {
        New-PM365Result `
            -Status 'Skipped' `
            -Code 'KeyVaultRecoveryCheckSkipped' `
            -Summary 'Key Vault recovery state was not checked in the wrong subscription.' `
            -Details "Select package subscription '$expectedSubscriptionId' before running this check."
        return
    }

    $vaultName = [string]$config.azure.resourceNames.keyVaultName
    $location = [string]$config.azure.location
    if ([string]::IsNullOrWhiteSpace($vaultName) -or [string]::IsNullOrWhiteSpace($location)) {
        New-PM365Result `
            -Status 'Failed' `
            -Code 'KeyVaultRecoveryContractMissing' `
            -Summary 'The package is missing the Key Vault name or Azure location.' `
            -Details 'Regenerate the signed package with complete Azure resource names.' `
            -RetrySafe $false
        return
    }

    $escapedLocation = [Uri]::EscapeDataString($location)
    $escapedVaultName = [Uri]::EscapeDataString($vaultName)
    $path = "/subscriptions/$actualSubscriptionId/providers/Microsoft.KeyVault/locations/$escapedLocation/deletedVaults/$escapedVaultName`?api-version=2022-07-01"

    try {
        $response = Invoke-AzRestMethod -Method GET -Path $path -ErrorAction Stop
        if ($response.StatusCode -eq 404) {
            New-PM365Result `
                -Status 'Passed' `
                -Code 'KeyVaultNameReady' `
                -Summary 'The package Key Vault name is not retained in soft delete.' `
                -Details $vaultName
            return
        }

        if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300) {
            throw "Azure Resource Manager returned HTTP $($response.StatusCode)."
        }

        $deletedVault = $response.Content | ConvertFrom-Json
        $scheduledPurgeDate = [string]$deletedVault.properties.scheduledPurgeDate
        New-PM365Result `
            -Status 'Failed' `
            -Code 'KeyVaultRecoveryRequired' `
            -Summary 'The package Key Vault name is retained in Azure soft delete.' `
            -Details "Recover '$vaultName' into the new target resource group, or regenerate the signed package with a new Key Vault name. Do not start deployment until this is resolved. Scheduled purge: $scheduledPurgeDate" `
            -RetrySafe $true `
            -Data @{
                vaultName = $vaultName
                location = $location
                scheduledPurgeDate = $scheduledPurgeDate
                recoveryId = [string]$deletedVault.properties.recoveryId
                purgeRecommended = $false
            }
    } catch {
        New-PM365Result `
            -Status 'Failed' `
            -Code 'KeyVaultRecoveryCheckUnavailable' `
            -Summary 'Key Vault soft-delete state could not be verified.' `
            -Details $_.Exception.Message `
            -RetrySafe $true
    }
}
