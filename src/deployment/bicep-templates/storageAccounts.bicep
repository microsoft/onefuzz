param owner string
param location string

var suffix = uniqueString(resourceGroup().id)
var storageAccountNameFuzz = 'fuzz${suffix}'
var storageAccountNameFunc = 'func${suffix}'

var storageAccountFuzzContainersParams = [
  'events'
]

var storageAccountFuncContainersParams = [
  'vm-scripts'
  'repro-scripts'
  'proxy-configs'
  'task-configs'
  'app-logs'
]

var storageAccountFuncQueuesParams = [
  'file-changes'
  'task-heartbeat'
  'node-heartbeat'
  'proxy'
  'update-queue'
  'webhooks'
  'signalr-events'
  'job-result'
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

resource storageAccountFuzzBlobContainers 'Microsoft.Storage/storageAccounts/blobServices/containers@2021-08-01' = [for c in storageAccountFuzzContainersParams: {
  name: '${storageAccountNameFuzz}/default/${c}'
  dependsOn: [
    storageAccountFuzz
  ]
}]

output FuzzName string = storageAccountNameFuzz
output FuncName string = storageAccountNameFunc

output FuzzId string = storageAccountFuzz.id
output FuncId string = storageAccountFunc.id

output FileChangesQueueName string = storageAccountFuncQueuesParams[fileChangesQueueIndex]
