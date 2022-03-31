param owner string
param location string
param signedExpiry string

var suffix = uniqueString(resourceGroup().id)
var storageAccountNameFuzz = 'fuzz${suffix}'
var storageAccountNameFunc = 'func${suffix}'


var storage_account_sas = {
  signedExpiry: signedExpiry
  signedPermission: 'rwdlacup'
  signedResourceTypes: 'sco'
  signedServices: 'bfqt'
}


var storageAccountFuncContainersParams = [
  'vm-scripts'
  'repro-scripts'
  'proxy-configs'
  'task-configs'
  'app-logs'
]

var storageAccountFuncQueuesParams = [
  'file-chages'
  'task-heartbeat'
  'node-heartbeat'
  'proxy'
  'update-queue'
  'webhooks'
  'signalr-events'
]
var fileChangesQueueIndex = 0

resource storageAccountFuzz 'Microsoft.Storage/storageAccounts@2021-08-01' = {
  name: storageAccountNameFuzz
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    accessTier: 'Hot'
    allowBlobPublicAccess: false
  }
  tags: {
    OWNER: owner
  }

  resource blobServices 'blobServices' = {
    name: 'default'
    properties: {
      deleteRetentionPolicy: {
        enabled: true
        days: 30
      }
    }
  }
}

resource storageAccountFunc 'Microsoft.Storage/storageAccounts@2021-08-01' = {
  name: storageAccountNameFunc
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    accessTier: 'Hot'
    allowBlobPublicAccess: false
  }
  tags: {
    OWNER: owner
  }

  resource blobServices 'blobServices' = {
    name: 'default'
    properties: {
      deleteRetentionPolicy: {
        enabled: true
        days: 30
      }
    }
  }
}

resource storageAccountFuncQueues 'Microsoft.Storage/storageAccounts/queueServices/queues@2021-08-01' = [for q in storageAccountFuncQueuesParams: {
  name: '${storageAccountNameFunc}/default/${q}'
  dependsOn: [
    storageAccountFunc
  ]
}]

resource storageAccountFunBlobContainers 'Microsoft.Storage/storageAccounts/blobServices/containers@2021-08-01' = [for c in storageAccountFuncContainersParams: {
  name: '${storageAccountNameFunc}/default/${c}'
  dependsOn: [
    storageAccountFunc
  ]
}]

output FuzzName string = storageAccountNameFuzz
output FuncName string = storageAccountNameFunc

output FuzzId string = storageAccountFuzz.id
output FuncId string = storageAccountFunc.id

output FileChangesQueueName string = storageAccountFuncQueuesParams[fileChangesQueueIndex]

var sas = storageAccountFunc.listAccountSas('2021-08-01', storage_account_sas)
output FuncSasUrlBlobAppLogs string = '${storageAccountFunc.properties.primaryEndpoints.blob}app-logs?${sas.accountSasToken}'

var fuzz_key = storageAccountFuzz.listKeys().keys[0].value
output FuzzKey string = fuzz_key

var func_key = storageAccountFunc.listKeys().keys[0].value
output FuncKey string = func_key

output FuncSasUrl string = 'DefaultEndpointsProtocol=https;AccountName=${storageAccountFunc.name};AccountKey=${func_key};EndpointSuffix=core.windows.net'
