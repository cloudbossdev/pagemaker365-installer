function New-PM365RuntimeConfigurationV2ApplicationInput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $CanonicalPackageJson,

        [Parameter(Mandatory)]
        [string] $TrustedSigningKeyId,

        [Parameter(Mandatory)]
        [string] $TrustedSigningPublicKeyPem,

        [Parameter(Mandatory)]
        [DateTimeOffset] $ValidationTime,

        [Parameter(Mandatory)]
        [string] $RuntimeConfigurationCatalogJson,

        [Parameter(Mandatory)]
        [string] $RuntimeConfigurationCatalogSchemaJson,

        [switch] $EnableRuntimeConfigurationProjectionV2
    )

    if (-not $EnableRuntimeConfigurationProjectionV2) {
        throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_disabled')
    }

    $requiredTypes = @(
        'PageMaker365.Installer.Engine.Models.PackageTrustOptions',
        'PageMaker365.Installer.Engine.Services.RuntimeConfigurationCatalogV1Authority',
        'PageMaker365.Installer.Engine.Services.PrivateRuntimeDeliveryV07PackageService',
        'PageMaker365.Installer.Engine.Services.RuntimeConfigurationApplicationV2Service'
    )
    $types = @{}
    foreach ($name in $requiredTypes) {
        $type = [AppDomain]::CurrentDomain.GetAssemblies() | ForEach-Object { $_.GetType($name, $false, $false) } | Where-Object { $null -ne $_ } | Select-Object -First 1
        if ($null -eq $type) {
            throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_engine_unavailable')
        }
        $types[$name] = $type
    }

    $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
    $catalog = $types['PageMaker365.Installer.Engine.Services.RuntimeConfigurationCatalogV1Authority']::Create(
        $utf8.GetBytes($RuntimeConfigurationCatalogJson),
        $utf8.GetBytes($RuntimeConfigurationCatalogSchemaJson))
    $keys = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    $keys.Add($TrustedSigningKeyId, $TrustedSigningPublicKeyPem)
    $trust = [Activator]::CreateInstance($types['PageMaker365.Installer.Engine.Models.PackageTrustOptions'])
    $trust.TrustedPublicKeysById = $keys
    $parser = [Activator]::CreateInstance(
        $types['PageMaker365.Installer.Engine.Services.PrivateRuntimeDeliveryV07PackageService'],
        @($catalog))
    $service = [Activator]::CreateInstance(
        $types['PageMaker365.Installer.Engine.Services.RuntimeConfigurationApplicationV2Service'],
        @($parser, $trust, $ValidationTime))
    $input = $service.CreateDeploymentInput($CanonicalPackageJson, $true)

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
    Assert-PM365RuntimeConfigurationV2TypedSettings -Items @($input.ApiPublicSettings) -TargetApp api -ExpectedNames $apiNames
    Assert-PM365RuntimeConfigurationV2TypedSettings -Items @($input.PortalPublicSettings) -TargetApp portal -ExpectedNames $portalNames

    $api = [ordered]@{}
    foreach ($setting in $input.ApiPublicSettings) {
        $api.Add($setting.Name, (ConvertTo-PM365RuntimeConfigurationV2TypedValue -Setting $setting))
    }
    $portal = [ordered]@{}
    foreach ($setting in $input.PortalPublicSettings) {
        $portal.Add($setting.Name, (ConvertTo-PM365RuntimeConfigurationV2TypedValue -Setting $setting))
    }

    $references = @($input.ApiVersionedProtectedSettingReferences)
    if ($references.Count -ne 2 -or $references[0].Name -cne 'DATABASE_URL' -or $references[1].Name -cne 'API_ENTRA_CLIENT_SECRET') {
        throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_protected_shape')
    }
    $versionedReferences = [ordered]@{
        DATABASE_URL = [string]$references[0].KeyVaultReference
        API_ENTRA_CLIENT_SECRET = [string]$references[1].KeyVaultReference
    }

    [ordered]@{
        enableRuntimeConfigurationProjectionV2 = $true
        apiRuntimeConfiguration = $api
        portalRuntimeConfiguration = $portal
        apiRuntimeConfigurationVersionedReferences = $versionedReferences
    }
}

function Assert-PM365RuntimeConfigurationV2TypedSettings {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object[]] $Items,
        [Parameter(Mandatory)] [string] $TargetApp,
        [Parameter(Mandatory)] [string[]] $ExpectedNames
    )
    if ($Items.Count -ne $ExpectedNames.Count) {
        throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_public_shape')
    }
    for ($index = 0; $index -lt $Items.Count; $index++) {
        if ([string]$Items[$index].TargetApp -cne $TargetApp -or [string]$Items[$index].Name -cne $ExpectedNames[$index]) {
            throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_public_shape')
        }
    }
}

function ConvertTo-PM365RuntimeConfigurationV2TypedValue {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Setting)

    $value = $Setting.Value
    switch -CaseSensitive ([string]$Setting.ValueType) {
        'string' {
            if ($value.ValueKind -ne [System.Text.Json.JsonValueKind]::String) { break }
            return $value.GetString()
        }
        'string-list' {
            if ($value.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) { break }
            $result = [System.Collections.Generic.List[string]]::new()
            foreach ($item in $value.EnumerateArray()) {
                if ($item.ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
                    throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_public_type')
                }
                $result.Add($item.GetString())
            }
            Write-Output -NoEnumerate ([string[]]$result.ToArray())
            return
        }
        'integer' {
            if ($value.ValueKind -ne [System.Text.Json.JsonValueKind]::Number) { break }
            $number = 0
            if (-not $value.TryGetInt32([ref]$number)) { break }
            return [int]$number
        }
        'boolean' {
            if ($value.ValueKind -notin @([System.Text.Json.JsonValueKind]::True, [System.Text.Json.JsonValueKind]::False)) { break }
            return [bool]$value.GetBoolean()
        }
    }
    throw [System.IO.InvalidDataException]::new('runtime_configuration_application_v2_public_type')
}
