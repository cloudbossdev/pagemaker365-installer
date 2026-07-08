@description('Storage account name.')
@minLength(3)
@maxLength(24)
param name string

@description('Azure region for the storage account.')
@minLength(1)
param location string

@description('Tags applied to the storage account.')
param tags object

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

output storageAccountResourceId string = storage.id
output storageAccountName string = storage.name
