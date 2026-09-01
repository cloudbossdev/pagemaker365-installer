@description('API App Service name.')
@minLength(1)
param name string

@description('Azure region for the API App Service.')
@minLength(1)
param location string

@description('Tags applied to the API App Service.')
param tags object

@description('Linux App Service plan resource ID.')
@minLength(1)
param appServicePlanId string

@description('User-assigned managed identity resource ID.')
@minLength(1)
param managedIdentityResourceId string

@description('Application Insights connection string.')
@minLength(1)
param applicationInsightsConnectionString string

@description('Key Vault URI used by the API.')
@minLength(1)
param keyVaultUri string

@description('Key Vault name used to construct App Service Key Vault references.')
@minLength(1)
param keyVaultName string

type RuntimeSecretReference = {
  appSettingName: string
  keyVaultSecretName: string
}

@sealed()
type ApiRuntimeConfigurationV2 = {
  API_APP_VERSION: string
  API_ENV: string
  API_HOST: string
  API_CORS_ORIGIN: string[]
  API_ENTRA_TENANT_ID: string
  API_ENTRA_AUDIENCE: string[]
  API_ENTRA_CLIENT_ID: string
  API_GRAPH_SCOPES: string[]
  API_REQUIRED_SCOPES: string[]
  API_SHAREPOINT_SITE_URL: string
  API_SHAREPOINT_UPLOADS_LIBRARY_NAME: string
  API_SHAREPOINT_ISSUES_LIST_NAME: string
  API_TENANT_CONNECTION_ID: string
  API_TENANT_DISPLAY_NAME: string
  PAGEMAKER365_PORTAL_URL: string
  API_LICENSE_PUBLIC_KEY_PEM: string
  API_LICENSE_ENVIRONMENT_KEY: string
  API_LICENSE_RUNTIME_HOSTNAME: string
  API_LICENSE_VALIDATION_GRACE_HOURS: int
  API_LICENSE_VALIDATION_INTERVAL_HOURS: int
  API_AZURE_KEY_VAULT_URL: string
  API_CONNECTOR_EGRESS_REQUIRE_ALLOWLIST: bool
  API_RUNTIME_TRUST_FORWARDED_HOST: bool
  NODE_ENV: string
  PM365_PRODUCT: string
  PM365_DEPLOYMENT_EXPORT_ID: string
  PM365_RUNTIME_RELEASE_ID: string
  PM365_RUNTIME_VERSION: string
  API_FILE_PREVIEW_ALLOWED_FRAME_ORIGINS: string[]
  API_FILE_PREVIEW_DOWNLOAD_POLICY: string
  API_FILE_PREVIEW_SOURCE_FALLBACK_POLICY: string
}

@sealed()
type ApiRuntimeConfigurationVersionedReferencesV2 = {
  DATABASE_URL: string
  API_ENTRA_CLIENT_SECRET: string
}

@description('Secret-name-only runtime App Service references.')
param runtimeSecretReferences RuntimeSecretReference[]

@description('Default-disabled package-0.7 runtime-configuration application gate.')
param enableRuntimeConfigurationProjectionV2 bool = false

@description('Exact validated projection-v2 API public settings. Null unless the application gate is enabled.')
param runtimeConfiguration ApiRuntimeConfigurationV2?

@description('Only already-versioned projection-v2 API Key Vault references. Pending license and cursor destinations are excluded.')
param runtimeConfigurationVersionedReferences ApiRuntimeConfigurationVersionedReferencesV2?

@description('Customer Entra tenant ID used for token validation and Microsoft Graph OBO.')
@minLength(36)
@maxLength(36)
param customerTenantId string

@description('Customer-owned API Entra application client ID.')
@minLength(36)
@maxLength(36)
param apiClientId string

@description('Exact portal origin allowed by API CORS.')
@minLength(1)
param portalOrigin string

@description('Exact customer SharePoint origin allowed for governed File Preview frames.')
@minLength(1)
param filePreviewAllowedFrameOrigins string

@description('Immutable PageMaker365 runtime release identifier.')
@minLength(1)
@maxLength(128)
param runtimeReleaseId string

@description('Stable semantic PageMaker365 runtime version.')
@minLength(5)
@maxLength(32)
param runtimeVersion string

@description('Signed control-plane deployment export identifier.')
@minLength(1)
@maxLength(256)
param deploymentExportId string

var runtimeSecretAppSettings = [for secret in runtimeSecretReferences: {
  name: secret.appSettingName
  value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${secret.keyVaultSecretName})'
}]

var legacyRuntimeAppSettings = concat([
  {
    name: 'NODE_ENV'
    value: 'production'
  }
  {
    name: 'API_ENV'
    value: 'production'
  }
  {
    name: 'API_HOST'
    value: '0.0.0.0'
  }
  {
    name: 'API_CORS_ORIGIN'
    value: portalOrigin
  }
  {
    name: 'API_ENTRA_TENANT_ID'
    value: customerTenantId
  }
  {
    name: 'API_ENTRA_AUDIENCE'
    value: 'api://${apiClientId}'
  }
  {
    name: 'API_ENTRA_CLIENT_ID'
    value: apiClientId
  }
  {
    name: 'API_AZURE_KEY_VAULT_URL'
    value: keyVaultUri
  }
  {
    name: 'API_FILE_PREVIEW_ALLOWED_FRAME_ORIGINS'
    value: filePreviewAllowedFrameOrigins
  }
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: applicationInsightsConnectionString
  }
  {
    name: 'PM365_PRODUCT'
    value: 'PageMaker365'
  }
  {
    name: 'PM365_DEPLOYMENT_EXPORT_ID'
    value: deploymentExportId
  }
  {
    name: 'PM365_RUNTIME_RELEASE_ID'
    value: runtimeReleaseId
  }
  {
    name: 'PM365_RUNTIME_VERSION'
    value: runtimeVersion
  }
  {
    name: 'SCM_DO_BUILD_DURING_DEPLOYMENT'
    value: 'false'
  }
  {
    name: 'ENABLE_ORYX_BUILD'
    value: 'false'
  }
], runtimeSecretAppSettings)

var projectionV2RuntimeAppSettings = [
  { name: 'API_APP_VERSION', value: runtimeConfiguration!.API_APP_VERSION }
  { name: 'API_ENV', value: runtimeConfiguration!.API_ENV }
  { name: 'API_HOST', value: runtimeConfiguration!.API_HOST }
  { name: 'API_CORS_ORIGIN', value: join(runtimeConfiguration!.API_CORS_ORIGIN, ',') }
  { name: 'API_ENTRA_TENANT_ID', value: runtimeConfiguration!.API_ENTRA_TENANT_ID }
  { name: 'API_ENTRA_AUDIENCE', value: join(runtimeConfiguration!.API_ENTRA_AUDIENCE, ',') }
  { name: 'API_ENTRA_CLIENT_ID', value: runtimeConfiguration!.API_ENTRA_CLIENT_ID }
  { name: 'API_GRAPH_SCOPES', value: join(runtimeConfiguration!.API_GRAPH_SCOPES, ',') }
  { name: 'API_REQUIRED_SCOPES', value: join(runtimeConfiguration!.API_REQUIRED_SCOPES, ',') }
  { name: 'API_SHAREPOINT_SITE_URL', value: runtimeConfiguration!.API_SHAREPOINT_SITE_URL }
  { name: 'API_SHAREPOINT_UPLOADS_LIBRARY_NAME', value: runtimeConfiguration!.API_SHAREPOINT_UPLOADS_LIBRARY_NAME }
  { name: 'API_SHAREPOINT_ISSUES_LIST_NAME', value: runtimeConfiguration!.API_SHAREPOINT_ISSUES_LIST_NAME }
  { name: 'API_TENANT_CONNECTION_ID', value: runtimeConfiguration!.API_TENANT_CONNECTION_ID }
  { name: 'API_TENANT_DISPLAY_NAME', value: runtimeConfiguration!.API_TENANT_DISPLAY_NAME }
  { name: 'PAGEMAKER365_PORTAL_URL', value: runtimeConfiguration!.PAGEMAKER365_PORTAL_URL }
  { name: 'API_LICENSE_PUBLIC_KEY_PEM', value: runtimeConfiguration!.API_LICENSE_PUBLIC_KEY_PEM }
  { name: 'API_LICENSE_ENVIRONMENT_KEY', value: runtimeConfiguration!.API_LICENSE_ENVIRONMENT_KEY }
  { name: 'API_LICENSE_RUNTIME_HOSTNAME', value: runtimeConfiguration!.API_LICENSE_RUNTIME_HOSTNAME }
  { name: 'API_LICENSE_VALIDATION_GRACE_HOURS', value: string(runtimeConfiguration!.API_LICENSE_VALIDATION_GRACE_HOURS) }
  { name: 'API_LICENSE_VALIDATION_INTERVAL_HOURS', value: string(runtimeConfiguration!.API_LICENSE_VALIDATION_INTERVAL_HOURS) }
  { name: 'API_AZURE_KEY_VAULT_URL', value: runtimeConfiguration!.API_AZURE_KEY_VAULT_URL }
  { name: 'API_CONNECTOR_EGRESS_REQUIRE_ALLOWLIST', value: runtimeConfiguration!.API_CONNECTOR_EGRESS_REQUIRE_ALLOWLIST ? 'true' : 'false' }
  { name: 'API_RUNTIME_TRUST_FORWARDED_HOST', value: runtimeConfiguration!.API_RUNTIME_TRUST_FORWARDED_HOST ? 'true' : 'false' }
  { name: 'NODE_ENV', value: runtimeConfiguration!.NODE_ENV }
  { name: 'PM365_PRODUCT', value: runtimeConfiguration!.PM365_PRODUCT }
  { name: 'PM365_DEPLOYMENT_EXPORT_ID', value: runtimeConfiguration!.PM365_DEPLOYMENT_EXPORT_ID }
  { name: 'PM365_RUNTIME_RELEASE_ID', value: runtimeConfiguration!.PM365_RUNTIME_RELEASE_ID }
  { name: 'PM365_RUNTIME_VERSION', value: runtimeConfiguration!.PM365_RUNTIME_VERSION }
  { name: 'API_FILE_PREVIEW_ALLOWED_FRAME_ORIGINS', value: join(runtimeConfiguration!.API_FILE_PREVIEW_ALLOWED_FRAME_ORIGINS, ',') }
  { name: 'API_FILE_PREVIEW_DOWNLOAD_POLICY', value: runtimeConfiguration!.API_FILE_PREVIEW_DOWNLOAD_POLICY }
  { name: 'API_FILE_PREVIEW_SOURCE_FALLBACK_POLICY', value: runtimeConfiguration!.API_FILE_PREVIEW_SOURCE_FALLBACK_POLICY }
  { name: 'DATABASE_URL', value: runtimeConfigurationVersionedReferences!.DATABASE_URL }
  { name: 'API_ENTRA_CLIENT_SECRET', value: runtimeConfigurationVersionedReferences!.API_ENTRA_CLIENT_SECRET }
]

var projectionV2PlatformAppSettings = [
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: applicationInsightsConnectionString
  }
  {
    name: 'SCM_DO_BUILD_DURING_DEPLOYMENT'
    value: 'false'
  }
  {
    name: 'ENABLE_ORYX_BUILD'
    value: 'false'
  }
]

resource apiApp 'Microsoft.Web/sites@2023-12-01' = {
  name: name
  location: location
  kind: 'app,linux'
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityResourceId}': {}
    }
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    keyVaultReferenceIdentity: managedIdentityResourceId
    siteConfig: {
      alwaysOn: true
      linuxFxVersion: 'NODE|22-lts'
      appCommandLine: 'node dist/index.js'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: enableRuntimeConfigurationProjectionV2 ? concat(projectionV2RuntimeAppSettings, projectionV2PlatformAppSettings) : legacyRuntimeAppSettings
    }
  }
}

output apiAppServiceResourceId string = apiApp.id
output apiAppServiceName string = apiApp.name
output apiDefaultHostName string = apiApp.properties.defaultHostName
output apiUrl string = 'https://${apiApp.properties.defaultHostName}'
