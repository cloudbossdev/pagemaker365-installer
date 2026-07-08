@description('Application Insights component name.')
@minLength(1)
param name string

@description('Azure region for Application Insights.')
@minLength(1)
param location string

@description('Tags applied to Application Insights.')
param tags object

@description('Log Analytics workspace resource ID backing Application Insights.')
@minLength(1)
param workspaceResourceId string

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: name
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspaceResourceId
  }
}

output applicationInsightsResourceId string = appInsights.id
output applicationInsightsName string = appInsights.name
output connectionString string = appInsights.properties.ConnectionString
output instrumentationKey string = appInsights.properties.InstrumentationKey
