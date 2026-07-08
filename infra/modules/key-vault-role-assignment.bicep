@description('Key Vault name that receives the role assignment.')
@minLength(1)
param keyVaultName string

@description('Resource ID used as a stable role assignment seed.')
@minLength(1)
param principalResourceId string

@description('Service principal object ID receiving Key Vault Secrets User.')
@minLength(1)
param principalId string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource keyVaultSecretUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, principalResourceId, 'Key Vault Secrets User')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

output roleAssignmentResourceId string = keyVaultSecretUserAssignment.id
