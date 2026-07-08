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
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        {
          name: 'NODE_ENV'
          value: 'production'
        }
        {
          name: 'VITE_API_BASE_URL'
          value: apiUrl
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsightsConnectionString
        }
      ]
    }
  }
}

output frontendAppServiceResourceId string = frontendApp.id
output frontendAppServiceName string = frontendApp.name
output frontendDefaultHostName string = frontendApp.properties.defaultHostName
output portalUrl string = 'https://${frontendApp.properties.defaultHostName}'
