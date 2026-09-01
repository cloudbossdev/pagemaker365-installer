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

type RuntimeApplicationSetting = {
  name: string
  value: string
}

@description('Secret-name-only runtime App Service references.')
param runtimeSecretReferences RuntimeSecretReference[]

@description('Default-disabled package-0.7 runtime-configuration application gate.')
param enableRuntimeConfigurationProjectionV2 bool = false

@description('Exact validated projection-v2 API public settings. Empty unless the application gate is enabled.')
param runtimeConfigurationPublicSettings RuntimeApplicationSetting[] = []

@description('Key-Vault-reference-only projection-v2 API protected settings. Empty unless the application gate is enabled.')
param runtimeConfigurationProtectedSettingReferences RuntimeApplicationSetting[] = []

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

var projectionV2RuntimeAppSettings = concat(runtimeConfigurationPublicSettings, runtimeConfigurationProtectedSettingReferences)

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
