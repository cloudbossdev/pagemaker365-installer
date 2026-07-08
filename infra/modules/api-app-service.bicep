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
    siteConfig: {
      alwaysOn: true
      linuxFxVersion: 'NODE|22-lts'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
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
      ]
    }
  }
}

output apiAppServiceResourceId string = apiApp.id
output apiAppServiceName string = apiApp.name
output apiDefaultHostName string = apiApp.properties.defaultHostName
output apiUrl string = 'https://${apiApp.properties.defaultHostName}'
