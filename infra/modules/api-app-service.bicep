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

@description('Secret-name-only runtime App Service references.')
param runtimeSecretReferences RuntimeSecretReference[]

@description('Immutable PageMaker365 runtime release identifier.')
@minLength(1)
param runtimeReleaseId string

@description('Stable semantic PageMaker365 runtime version.')
@minLength(5)
param runtimeVersion string

@description('Signed control-plane deployment export identifier.')
@minLength(1)
param deploymentExportId string

var runtimeSecretAppSettings = [for secret in runtimeSecretReferences: {
  name: secret.appSettingName
  value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${secret.keyVaultSecretName})'
}]

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
      appSettings: concat([
        {
          name: 'NODE_ENV'
          value: 'production'
        }
        {
          name: 'API_HOST'
          value: '0.0.0.0'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsightsConnectionString
        }
        {
          name: 'PM365_KEY_VAULT_URI'
          value: keyVaultUri
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
    }
  }
}

output apiAppServiceResourceId string = apiApp.id
output apiAppServiceName string = apiApp.name
output apiDefaultHostName string = apiApp.properties.defaultHostName
output apiUrl string = 'https://${apiApp.properties.defaultHostName}'
