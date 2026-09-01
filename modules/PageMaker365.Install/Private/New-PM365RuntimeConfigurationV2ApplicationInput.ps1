function New-PM365RuntimeConfigurationV2ApplicationInput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $PlanJson,

        [Parameter(Mandatory)]
        [string] $ExpectedPlanSha256,

        [switch] $EnableRuntimeConfigurationProjectionV2
    )

    if (-not $EnableRuntimeConfigurationProjectionV2) {
        throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_disabled')
    }
    if ($ExpectedPlanSha256 -cnotmatch '^[0-9a-f]{64}$') {
        throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_plan_hash')
    }

    $bytes = [System.Text.UTF8Encoding]::new($false, $true).GetBytes($PlanJson)
    $actualHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    if (-not [System.Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
        [System.Text.Encoding]::ASCII.GetBytes($ExpectedPlanSha256),
        [System.Text.Encoding]::ASCII.GetBytes($actualHash))) {
        throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_plan_hash')
    }
    if ($PlanJson.Contains("`r") -or -not $PlanJson.EndsWith("`n", [StringComparison]::Ordinal)) {
        throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_plan_canonical')
    }

    $document = $null
    $plan = $null
    try {
        $document = [System.Text.Json.JsonDocument]::Parse($PlanJson)
        $plan = $PlanJson | ConvertFrom-Json -Depth 32
    } catch {
        throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_plan_json')
    }

    try {
        $rootNames = @($document.RootElement.EnumerateObject() | ForEach-Object { $_.Name })
        $expectedRootNames = @(
            'contractVersion', 'packageHash', 'projectionSha256', 'binding', 'apiPublicSettings',
            'portalPublicSettings', 'apiProtectedSettingReferences', 'licenseAcquisition',
            'cursorGeneration', 'rollback'
        )
        if (-not (@($rootNames).Count -eq $expectedRootNames.Count -and
            -not (Compare-Object $expectedRootNames $rootNames -SyncWindow 0))) {
            throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_plan_shape')
        }
        if ([string]$plan.contractVersion -cne 'pagemaker365.runtime-configuration-application.v2' -or
            [string]$plan.packageHash -cnotmatch '^sha256:[0-9a-f]{64}$' -or
            [string]$plan.projectionSha256 -cnotmatch '^[0-9a-f]{64}$') {
            throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_plan_identity')
        }

        $bindingNames = @($document.RootElement.GetProperty('binding').EnumerateObject() | ForEach-Object { $_.Name })
        $expectedBindingNames = @(
            'customerId', 'installationId', 'environmentId', 'tenantId', 'azureSubscriptionId',
            'deploymentExportId', 'runtimeReleaseId', 'runtimeVersion', 'manifestSha256'
        )
        if (Compare-Object $expectedBindingNames $bindingNames -SyncWindow 0) {
            throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_binding')
        }

        $apiNames = @(
            'API_APP_VERSION', 'API_ENV', 'API_HOST', 'API_CORS_ORIGIN', 'API_ENTRA_TENANT_ID',
            'API_ENTRA_AUDIENCE', 'API_ENTRA_CLIENT_ID', 'API_GRAPH_SCOPES', 'API_REQUIRED_SCOPES',
            'API_SHAREPOINT_SITE_URL', 'API_SHAREPOINT_UPLOADS_LIBRARY_NAME', 'API_SHAREPOINT_ISSUES_LIST_NAME',
            'API_TENANT_CONNECTION_ID', 'API_TENANT_DISPLAY_NAME', 'PAGEMAKER365_PORTAL_URL',
            'API_LICENSE_PUBLIC_KEY_PEM', 'API_LICENSE_ENVIRONMENT_KEY', 'API_LICENSE_RUNTIME_HOSTNAME',
            'API_LICENSE_VALIDATION_GRACE_HOURS', 'API_LICENSE_VALIDATION_INTERVAL_HOURS', 'API_AZURE_KEY_VAULT_URL',
            'API_CONNECTOR_EGRESS_REQUIRE_ALLOWLIST', 'API_RUNTIME_TRUST_FORWARDED_HOST', 'NODE_ENV', 'PM365_PRODUCT',
            'PM365_DEPLOYMENT_EXPORT_ID', 'PM365_RUNTIME_RELEASE_ID', 'PM365_RUNTIME_VERSION',
            'API_FILE_PREVIEW_ALLOWED_FRAME_ORIGINS', 'API_FILE_PREVIEW_DOWNLOAD_POLICY',
            'API_FILE_PREVIEW_SOURCE_FALLBACK_POLICY'
        )
        $portalNames = @(
            'WEB_API_BASE_URL', 'WEB_ENTRA_CLIENT_ID', 'WEB_ENTRA_TENANT_ID', 'WEB_ENTRA_AUTHORITY', 'WEB_API_SCOPE',
            'WEB_RUNTIME_ENVIRONMENT', 'WEB_PRODUCT_NAME', 'WEB_PRODUCT_LOGO_URL', 'WEB_CUSTOMER_DISPLAY_NAME',
            'WEB_CUSTOMER_SHORT_NAME', 'WEB_FILE_PREVIEW_ALLOWED_FRAME_ORIGINS'
        )
        Assert-PM365RuntimeConfigurationV2Settings -Items @($plan.apiPublicSettings) -ExpectedNames $apiNames
        Assert-PM365RuntimeConfigurationV2Settings -Items @($plan.portalPublicSettings) -ExpectedNames $portalNames

        $protected = @($plan.apiProtectedSettingReferences)
        $protectedNames = @('DATABASE_URL', 'API_ENTRA_CLIENT_SECRET', 'API_LICENSE_SIGNED_PAYLOAD', 'API_IMAGE_ASSET_CURSOR_SECRET')
        $protectedModes = @(
            'customer-azure-key-vault-reference', 'customer-azure-key-vault-reference',
            'control-plane-protected-setting-delivery', 'installer-generated-key-vault-secret'
        )
        if ($protected.Count -ne 4) {
            throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_protected_shape')
        }
        for ($index = 0; $index -lt $protected.Count; $index++) {
            $names = @($protected[$index].PSObject.Properties.Name)
            if ((Compare-Object @('name', 'mode', 'keyVaultReference') $names -SyncWindow 0) -or
                [string]$protected[$index].name -cne $protectedNames[$index] -or
                [string]$protected[$index].mode -cne $protectedModes[$index] -or
                [string]$protected[$index].keyVaultReference -cnotmatch '^@Microsoft\.KeyVault\(SecretUri=https://[a-z0-9-]{3,24}\.vault\.azure\.net/secrets/[A-Za-z0-9-]{1,127}(?:/[0-9a-f]{32})?\)$') {
                throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_protected_shape')
            }
            if ($index -lt 2 -and [string]$protected[$index].keyVaultReference -cnotmatch '/[0-9a-f]{32}\)$') {
                throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_protected_version')
            }
            if ($index -ge 2 -and [string]$protected[$index].keyVaultReference -cmatch '/[0-9a-f]{32}\)$') {
                throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_pending_reference')
            }
        }

        if ((Compare-Object @('contractVersion', 'opaqueReference', 'vaultResourceId', 'secretName') @($plan.licenseAcquisition.PSObject.Properties.Name) -SyncWindow 0) -or
            (Compare-Object @('generationAlgorithm', 'minimumEntropyBytes', 'vaultResourceId', 'secretName') @($plan.cursorGeneration.PSObject.Properties.Name) -SyncWindow 0) -or
            (Compare-Object @('strategy', 'targetQualifiedSettings', 'containsValues') @($plan.rollback.PSObject.Properties.Name) -SyncWindow 0) -or
            [string]$plan.licenseAcquisition.contractVersion -cne 'pagemaker365.protected-setting-acquisition.v1' -or
            [string]$plan.licenseAcquisition.opaqueReference -cnotmatch '^psr_[A-Za-z0-9_-]{24,64}$' -or
            [string]$plan.licenseAcquisition.vaultResourceId -cnotmatch '^/subscriptions/[0-9a-f-]{36}/resourceGroups/[A-Za-z0-9._()-]{1,90}/providers/Microsoft\.KeyVault/vaults/[A-Za-z0-9-]{3,24}$' -or
            [string]$plan.licenseAcquisition.secretName -cnotmatch '^[A-Za-z0-9-]{1,127}$' -or
            [string]$plan.cursorGeneration.generationAlgorithm -cne 'random-base64url' -or
            [int]$plan.cursorGeneration.minimumEntropyBytes -ne 32 -or
            [string]$plan.cursorGeneration.vaultResourceId -cnotmatch '^/subscriptions/[0-9a-f-]{36}/resourceGroups/[A-Za-z0-9._()-]{1,90}/providers/Microsoft\.KeyVault/vaults/[A-Za-z0-9-]{3,24}$' -or
            [string]$plan.cursorGeneration.secretName -cnotmatch '^[A-Za-z0-9-]{1,127}$' -or
            [string]$plan.rollback.strategy -cne 'restore-previous-app-setting-state' -or
            [bool]$plan.rollback.containsValues) {
            throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_pending_descriptor')
        }
        if (@($plan.rollback.targetQualifiedSettings).Count -ne 46) {
            throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_rollback')
        }

        [ordered]@{
            enableRuntimeConfigurationProjectionV2 = $true
            apiRuntimeConfigurationPublicSettings = @($plan.apiPublicSettings | ForEach-Object { [ordered]@{ name = [string]$_.name; value = [string]$_.value } })
            portalRuntimeConfigurationPublicSettings = @($plan.portalPublicSettings | ForEach-Object { [ordered]@{ name = [string]$_.name; value = [string]$_.value } })
            apiRuntimeConfigurationProtectedSettingReferences = @($protected | ForEach-Object { [ordered]@{ name = [string]$_.name; value = [string]$_.keyVaultReference } })
        }
    } finally {
        if ($document) { $document.Dispose() }
        $bytes = $null
        $plan = $null
    }
}

function Assert-PM365RuntimeConfigurationV2Settings {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object[]] $Items,

        [Parameter(Mandatory)]
        [string[]] $ExpectedNames
    )

    if ($Items.Count -ne $ExpectedNames.Count) {
        throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_public_shape')
    }
    for ($index = 0; $index -lt $Items.Count; $index++) {
        $names = @($Items[$index].PSObject.Properties.Name)
        if ((Compare-Object @('name', 'value') $names -SyncWindow 0) -or
            [string]$Items[$index].name -cne $ExpectedNames[$index] -or
            $Items[$index].value -isnot [string]) {
            throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_public_shape')
        }
    }
}
