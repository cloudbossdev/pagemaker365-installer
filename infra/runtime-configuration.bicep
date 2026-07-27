targetScope = 'resourceGroup'

@description('Existing customer Key Vault name.')
@minLength(1)
param keyVaultName string

@description('Runtime secret values supplied only as a secure in-memory deployment parameter.')
@secure()
param runtimeSecrets object

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource runtimeSecretResources 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = [for secret in runtimeSecrets.items: {
  parent: keyVault
  name: secret.keyVaultSecretName
  properties: {
    value: secret.value
    contentType: 'PageMaker365 runtime configuration'
  }
  tags: {
    managedBy: 'PageMaker365'
    owner: 'customer'
  }
}]

output configuredSecretCount int = length(runtimeSecretResources)
