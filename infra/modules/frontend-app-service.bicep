@description('Frontend App Service name.')
@minLength(1)
param name string

@description('Azure region for the frontend App Service.')
@minLength(1)
param location string

@description('Tags applied to the frontend App Service.')
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

@description('Base URL for the API App Service.')
@minLength(1)
param apiUrl string

@description('Customer Entra tenant ID used by the portal runtime.')
@minLength(36)
@maxLength(36)
param customerTenantId string

@description('Customer-owned portal Entra application client ID.')
@minLength(36)
@maxLength(36)
param portalClientId string

@description('Customer-owned API Entra application client ID.')
@minLength(36)
@maxLength(36)
param apiClientId string

@description('Hosted portal runtime environment.')
@allowed([
  'dev'
  'staging'
  'production'
])
param runtimeEnvironment string

@description('Customer display name exposed by the portal runtime.')
@minLength(1)
@maxLength(128)
param customerDisplayName string

@description('Customer short name exposed by the portal runtime.')
@minLength(1)
@maxLength(64)
param customerShortName string

@description('Exact customer SharePoint origin allowed for governed File Preview frames.')
@minLength(1)
param filePreviewAllowedFrameOrigins string

@description('Immutable PageMaker365 runtime release identifier.')
@minLength(1)
@maxLength(128)
param runtimeReleaseId string

type RuntimeApplicationSetting = {
  name: string
  value: string
}

@description('Default-disabled package-0.7 runtime-configuration application gate.')
param enableRuntimeConfigurationProjectionV2 bool = false

@description('Exact validated projection-v2 portal public settings. Empty unless the application gate is enabled.')
param runtimeConfigurationPublicSettings RuntimeApplicationSetting[] = []

var projectionV2RuntimeAppSettings = runtimeConfigurationPublicSettings

var legacyRuntimeAppSettings = [
  {
    name: 'NODE_ENV'
    value: 'production'
  }
  {
    name: 'WEB_API_BASE_URL'
    value: apiUrl
  }
  {
    name: 'WEB_ENTRA_CLIENT_ID'
    value: portalClientId
  }
  {
    name: 'WEB_ENTRA_TENANT_ID'
    value: customerTenantId
  }
  {
    name: 'WEB_ENTRA_AUTHORITY'
    value: 'https://login.microsoftonline.com/${customerTenantId}'
  }
  {
    name: 'WEB_API_SCOPE'
    value: 'api://${apiClientId}/access_as_user'
  }
  {
    name: 'WEB_RUNTIME_ENVIRONMENT'
    value: runtimeEnvironment
  }
  {
    name: 'WEB_PRODUCT_NAME'
    value: 'PageMaker365'
  }
  {
    name: 'WEB_PRODUCT_LOGO_URL'
    value: '/branding/pagemaker365-logo.png'
  }
  {
    name: 'WEB_CUSTOMER_DISPLAY_NAME'
    value: customerDisplayName
  }
  {
    name: 'WEB_CUSTOMER_SHORT_NAME'
    value: customerShortName
  }
  {
    name: 'WEB_ENABLE_WEB_PART_WORKBENCH'
    value: 'false'
  }
  {
    name: 'WEB_FILE_PREVIEW_ALLOWED_FRAME_ORIGINS'
    value: filePreviewAllowedFrameOrigins
  }
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: applicationInsightsConnectionString
  }
  {
    name: 'PM365_RUNTIME_RELEASE_ID'
    value: runtimeReleaseId
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

resource frontendApp 'Microsoft.Web/sites@2023-12-01' = {
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
    siteConfig: {
      alwaysOn: true
      linuxFxVersion: 'NODE|22-lts'
      appCommandLine: 'node .pm365/start-portal-runtime.mjs'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: enableRuntimeConfigurationProjectionV2 ? concat(projectionV2RuntimeAppSettings, projectionV2PlatformAppSettings) : legacyRuntimeAppSettings
    }
  }
}

output frontendAppServiceResourceId string = frontendApp.id
output frontendAppServiceName string = frontendApp.name
output frontendDefaultHostName string = frontendApp.properties.defaultHostName
output portalUrl string = 'https://${frontendApp.properties.defaultHostName}'
