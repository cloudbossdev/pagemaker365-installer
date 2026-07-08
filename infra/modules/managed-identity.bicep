@description('User-assigned managed identity name.')
@minLength(1)
param name string

@description('Azure region for the managed identity.')
@minLength(1)
param location string

@description('Tags applied to the managed identity.')
param tags object

resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: name
  location: location
  tags: tags
}

output managedIdentityResourceId string = managedIdentity.id
output managedIdentityName string = managedIdentity.name
output managedIdentityPrincipalId string = managedIdentity.properties.principalId
