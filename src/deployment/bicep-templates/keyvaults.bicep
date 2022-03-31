param tenant_id string
param principal_id string
param location string

var keyVaultName = 'of-kv-${uniqueString(resourceGroup().id)}'

resource keyVault 'Microsoft.KeyVault/vaults@2021-10-01' = {
  name: keyVaultName
  location: location
  properties: {
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: true
    sku: {
      family: 'A'
      name: 'standard'
    }
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
    tenantId: tenant_id
    accessPolicies: [
      {
        objectId: principal_id
        tenantId: tenant_id
        permissions: {
          secrets: [
            'get'
            'list'
            'set'
            'delete'
          ]
        }
      }
    ]
  }
}

output name string = keyVaultName
