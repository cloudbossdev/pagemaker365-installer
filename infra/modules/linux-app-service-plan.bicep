@description('Linux App Service plan name.')
@minLength(1)
param name string

@description('Azure region for the Linux App Service plan.')
@minLength(1)
param location string

@description('Tags applied to the Linux App Service plan.')
param tags object

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: name
  location: location
  kind: 'linux'
  tags: tags
  sku: {
    name: 'B1'
    tier: 'Basic'
    capacity: 1
  }
  properties: {
    reserved: true
  }
}

output appServicePlanResourceId string = appServicePlan.id
output appServicePlanName string = appServicePlan.name
