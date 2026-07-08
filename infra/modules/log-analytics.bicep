@description('Log Analytics workspace name.')
@minLength(4)
param name string

@description('Azure region for the Log Analytics workspace.')
@minLength(1)
param location string

@description('Tags applied to the Log Analytics workspace.')
param tags object

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

output workspaceResourceId string = logAnalytics.id
output workspaceName string = logAnalytics.name
