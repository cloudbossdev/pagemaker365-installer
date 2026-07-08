@description('Key Vault name.')
@minLength(1)
param name string

@description('Azure region for Key Vault.')
@minLength(1)
param location string

@description('Tags applied to Key Vault.')
param tags object

@description('Tenant ID used by Key Vault.')
@minLength(36)
@maxLength(36)
param tenantId string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enabledForTemplateDeployment: false
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    publicNetworkAccess: 'Enabled'
  }
}

output keyVaultResourceId string = keyVault.id
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
