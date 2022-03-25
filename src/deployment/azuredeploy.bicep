param name string
param owner string
param clientId string
param clientSecret string
param signedExpiry string
param app_func_issuer string
param app_func_audience string
param multi_tenant_domain string

param location string = resourceGroup().location

@description('Azure monitor workbook definitions.')
param workbookData object

@description('The degree of severity for diagnostics logs.')
@allowed([
  'Verbose'
  'Information'
  'Warning'
  'Error'
])
param diagnosticsLogLevel string = 'Verbose'

var suffix = uniqueString(resourceGroup().id)
var tenantId = subscription().tenantId

var autoscale_name = 'onefuzz-autoscale-${suffix}' 
var log_retention = 30
var monitorAccountName = 'logs-wb-${suffix}'
var scaleset_identity = '${name}-scalesetid'
var signalr_name = 'onefuzz-${suffix}'
var storage_account_sas = {
  signedExpiry: signedExpiry
  signedPermissions: 'rwdlacup'
  signedResrouceTypes: 'sco'
  signedServices: 'bfqt'
}

var storageAccountName = 'fuzz${suffix}'
var storageAccountNameFunc = 'func${suffix}'
var telemetry = 'd7a73cf4-5a1a-4030-85e1-e5b25867e45a'
var StorageBlobDataReader = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
var keyVaultName = 'of-kv-${suffix}'
var fuzz_blob_topic_name ='fuzz-blob-topic-${suffix}'

var roleAssignmentsParams = [
  {
    suffix: '-vmss'
    role: '9980e02c-c2be-4d73-94e8-173b1dc7cf3c' //VirtualMachineContributor
  }
  {
    suffix: '-storage'
    role:'17d1049b-9a84-46fb-8f53-869881c3d3ab' //StorageAccountContributor
  }
  {
    suffix: '-network'
    role: '4d97b98b-1d4f-4787-a291-c67834d212e7'//NetworkContributor
  }
  {
    suffix: '-logs'
    role: '92aaf0da-9dab-42b6-94a3-d43ce8d16293'//LogAnalyticsContributor
  }
  {
    suffix: '-user_managed_identity'
    role: 'f1a07417-d97a-45cb-824c-7a7467783830'//ManagedIdentityOperator
  }
  {
    suffix: '-contributor'
    role: 'b24988ac-6180-42a0-ab88-20f7382dd24c'//Contributor
  }
]

var onefuzz = {
  severitiesAtMostInfo: {
    parameters: []
    output: {
      type: 'array'
      value: [
        {
          severity: 'emerg'
        }
        {
            severity: 'alert'
        }
        {
            severity: 'crit'
        }
        {
            severity: 'err'
        }
        {
            severity: 'warning'
        }
        {
            severity: 'notice'
        }
        {
            severity: 'info'
        }
      ]
    }
  }
}

resource scalesetIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2018-11-30' = {
  name: scaleset_identity
  location: location
}

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
    tenantId: tenantId
    accessPolicies: [
      {
        objectId: reference(resourceId('Microsoft.Web/sites', name), '2019-08-01', 'full').identity.principalId
        tenantId: tenantId
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

resource serverFarms 'Microsoft.Web/serverfarms@2021-03-01' = {
  name: name
  location: location
  kind: 'linux'
  properties: {
    reserved: true
  }
  sku: {
    name: 'P2v2'
    tier: 'PremiumV2'
    family: 'Pv2'
    capacity: 1
  }
  tags: {
    OWNER: owner
  }
}

resource autoscaleSettings 'Microsoft.Insights/autoscalesettings@2015-04-01' = {
  name: autoscale_name
  location: location 
  properties: {
    name: autoscale_name
    enabled: true
    targetResourceUri: serverFarms.id
    targetResourceLocation: location
    notifications: []
    profiles:[
      {
        name: 'Auto scale condition'
        capacity: {
          default: '1'
          maximum: '20'
          minimum: '1'
        }
        rules: [
          {
            metricTrigger: {
              metricName: 'CpuPercentage' 
              metricResourceUri: serverFarms.id
              operator: 'GreaterThanOrEqual'
              statistic: 'Average'
              threshold: 20
              timeAggregation: 'Average'
              timeGrain: 'PT1M'
              timeWindow: 'PT1M'
            }
            scaleAction: {
              cooldown: 'PT1M'
              direction: 'Increase'
              type: 'ChangeCount'
              value: '5'
            }
          }
          {
            metricTrigger: {
              metricName: 'CpuPercentage'
              metricResourceUri: serverFarms.id
              operator: 'LessThan'
              statistic: 'Average'
              threshold: 20
              timeAggregation:'Average' 
              timeGrain: 'PT1M'
              timeWindow: 'PT1M'
            }
            scaleAction: {
              cooldown: 'PT5M'
              direction: 'Decrease'
              type: 'ChangeCount'
              value: '1'
            }
          }

        ]
      }
    ]
  }
  tags: {
    OWNER: owner
  }
}

resource insightsWorkspace 'Microsoft.OperationalInsights/workspaces@2021-06-01' = {
  name: name
  location: location
}

var linuxDataSources = [
  {
    name: 'syslogDataSourcesKern'
    syslogName: 'kern'
    kind: 'LinuxSyslog'
  }
  {
    name: 'syslogDataSourcesUser'
    syslogName: 'user'
    kind: 'LinuxSyslog'
  }
  {
    name: 'syslogDataSourcesCron'
    syslogName: 'cron'
    kind: 'LinuxSyslog'
  }
  {
    name: 'syslogDataSourcesDaemon'
    syslogName: 'daemon'
    kind: 'LinuxSyslog'
  }
]

var windowsDataSources = [
  {
    name: 'windowsEventSystem'
    eventLogName: 'System'
    kind: 'WindowsEvent'
  }
  {
    name: 'windowsEventApplication'
    eventLogName: 'Application'
    kind: 'WindowsEvent'
  }
]

resource insightsMonitorAccount 'Microsoft.OperationalInsights/workspaces@2021-06-01' = {
  name: monitorAccountName
  location: location 
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: log_retention
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
  resource linux 'dataSources@2020-08-01' = [for d in linuxDataSources : {
    name: d.name
    kind: d.kind
    properties: {
      syslogName: d.syslogName
      syslogSeverities: onefuzz.severitiesAtMostInfo
    }
  }]

  resource linuxCollection 'dataSources@2020-08-01' = {
    name: 'syslogDataSourceCollection'
    kind: 'LinuxSyslogCollection'
    properties: {
      state: 'Enabled'
    }
  }

  resource windows 'dataSources@2020-08-01' = [for d in windowsDataSources : {
    name: d.name
    kind: d.kind
    properties: {
      eventLogName: d.eventLogName
      eventTypes: [
        {
          eventType: 'Error'
        }
        {
          eventType: 'Warning'
        }
        {
          eventType: 'Information'
        }
      ]
    }
  }]
}

resource vmInsights 'Microsoft.OperationsManagement/solutions@2015-11-01-preview' = {
  name: 'VMInsights(${monitorAccountName})'
  dependsOn: [
    insightsMonitorAccount
  ]
  plan: {
    name: 'VMInsights(${monitorAccountName})'
    publisher: 'Microsoft'
    product: 'OMSGallery/VMInsights'
    promotionCode: ''
  }
}

resource insightsComponents 'Microsoft.Insights/components@2020-02-02' = {
  name: name
  location: location
  kind: ''
  properties: {
    Application_Type: 'other'
    RetentionInDays: log_retention
    WorkspaceResourceId: insightsWorkspace.id
  }
  tags: {
    OWNER: owner
  }
}

resource insightsWorkbooks 'Microsoft.Insights/workbooks@2021-08-01' = {
  name: 'df20765c-ed5b-46f9-a47b-20f4aaf7936d'
  location: location
  kind: 'shared'
  properties: {
    displayName: 'Libfuzzer Job Dashboard'
    serializedData: workbookData.libFuzzerJob
    version: '1.0'
    sourceId: insightsComponents.id
    category: 'tsg'
  }
}

var storageAccountsParams = [
  {
    name: storageAccountName
  }
  {
    name: storageAccountNameFunc
  }
]

var storageAccountIndex = 0
var storageAccountFuncIndex = 1
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

resource storageAccounts 'Microsoft.Storage/storageAccounts@2021-08-01' = [for p in storageAccountsParams : {
  name: p.name
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
}]

resource blobServices 'Microsoft.Storage/storageAccounts/blobServices@2021-08-01' = [for (p,i) in storageAccountsParams : {
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 30
    }
  }
  parent: storageAccounts[i]
}]

resource storageAccountFuncQueueServices 'Microsoft.Storage/storageAccounts/queueServices@2021-08-01' = {
  name: 'funcQueues'
  parent: storageAccounts[storageAccountFuncIndex]
}

resource storageAccountFunBlobContainers 'Microsoft.Storage/storageAccounts/blobServices/containers@2021-08-01' = [for c in storageAccountFuncContainersParams: {
  name: c
  parent: blobServices[storageAccountFuncIndex]
}]

resource storageAccountFuncQueues 'Microsoft.Storage/storageAccounts/queueServices/queues@2021-08-01' = [for q in storageAccountFuncQueuesParams: {
  name: q
  parent: storageAccountFuncQueueServices
}]

resource roleAssigments 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = [for r in roleAssignmentsParams: {
  name: guid('${resourceGroup().id}${r.suffix}')
  properties: {
    roleDefinitionId: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/${r.role}'
    principalId: reference(resourceId('Microsoft.Web/sites', name), '2018-02-01', 'Full').identity.principalId
  }
  dependsOn: [
    pythonFunction
  ]
}]

//this gets configured differently from roleAssignments
resource readBlogUserAssignment 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid('${resourceGroup().id}-user_managed_idenity_read_blob')
  properties: {
    roleDefinitionId: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/${StorageBlobDataReader}'
    principalId: reference(resourceId('Microsoft.ManagedIdentity/userAssignedIdentities', scaleset_identity), '2018-11-30', 'Full').properties.principalId
  }
  dependsOn: [
    storageAccounts[storageAccountFuncIndex]
  ]
}

resource signalR 'Microsoft.SignalRService/signalR@2021-10-01' = {
  name: signalr_name
  location: location
  sku: {
    name: 'Standard_S1'
    tier: 'Standard'
    capacity: 1
  }
  properties: {
    features: [
      {
          flag: 'ServiceMode'
          value: 'Serverless'
          properties: {}
      }
      {
          flag: 'EnableConnectivityLogs'
          value: 'True'
          properties: {}
      }
      {
          flag: 'EnableMessagingLogs'
          value: 'False'
          properties: {}
      }
    ]
  }
}

resource eventGridSystemTopics 'Microsoft.EventGrid/systemTopics@2021-12-01' = {
  name: fuzz_blob_topic_name
  location: location
  properties: {
    source: storageAccounts[storageAccountIndex].id
    topicType: 'microsoft.storage.storageaccounts'
  }
  resource evetnSubscriptions 'eventSubscriptions' = {
    name: 'onefuzz1_subscription'
    properties: {
      destination: {
        properties: {
          resourceId: storageAccounts[storageAccountFuncIndex].id
          queueName: 'file-changes'
        }
        endpointType: 'StorageQueue'
      }
      filter: {
        includedEventTypes: [
          'Microsoft.Storage.BlobCreated'
          'Microsoft.Storage.BlobDeleted'
        ]
      }
      eventDeliverySchema: 'EventGridSchema'
      retryPolicy: {
        maxDeliveryAttempts: 30
        eventTimeToLiveInMinutes: 1440
      }
    }
  }
}

resource funcLogs 'Microsoft.Web/sites/config@2021-03-01' = {
  name: 'logs'
  properties: {
    applicationLogs: {
      azureBlobStorage: {
        level: diagnosticsLogLevel
        retentionInDays: log_retention
        sasUrl: '${storageAccounts[storageAccountIndex].properties.primaryEndpoints.blob}app-logs?${storageAccounts[storageAccountFuncIndex].listAccountSas('2021-08-01', storage_account_sas).accountSasToken}'
      }
    }
  }
  parent: pythonFunction
}

resource funcAuthSettings 'Microsoft.Web/sites/config@2021-03-01' = {
  name: 'authsettingsV2'
  properties: {
    login:{
      tokenStore: {
        enabled: true
      }
    }
    globalValidation: {
      unauthenticatedClientAction: 'RedirectToLoginPage'
      requireAuthentication: true
    }
    httpSettings: {
      requireHttps: true
    }
    identityProviders: {
      azureActiveDirectory: {
        enabled: true
        isAutoProvisioned: false
        registration: {
          clientId: clientId
          openIdIssuer: app_func_issuer
          clientSecretSettingName: 'ONEFUZZ_CLIENT_SECRET'
        }
        validation: {
          allowedAudiences: [
            app_func_audience
          ]
        }
      }
    }
  }
  parent: pythonFunction
}

resource pythonFunction 'Microsoft.Web/sites@2021-03-01' = {
  name: name
  location: location
  kind: 'functionapp,linux'
  tags: {
    'OWNER': owner
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    siteConfig: {
      appSettings: [
          {
              name: 'FUNCTIONS_EXTENSION_VERSION'
              value: '~3'
          }
          {
              name: 'FUNCTIONS_WORKER_RUNTIME'
              value: 'python'
          }
          {
              name: 'FUNCTIONS_WORKER_PROCESS_COUNT'
              value: '1'
          }
          {
              name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
              value: insightsComponents.properties.InstrumentationKey
          }
          {
              name: 'APPINSIGHTS_APPID'
              value: insightsComponents.properties.AppId
          }
          {
              name: 'ONEFUZZ_TELEMETRY'
              value: telemetry
          }
          {
              name: 'AzureWebJobsStorage'
              value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccounts[storageAccountFuncIndex].name};AccountKey=${storageAccounts[storageAccountFuncIndex].listKeys().keys[0]};EndpointSuffix=core.windows.net'
          }
          {
              name: 'MULTI_TENANT_DOMAIN'
              value: multi_tenant_domain
          }
          {
              name: 'AzureWebJobsDisableHomepage'
              value: 'true'
          }
          {
              name: 'AzureSignalRConnectionString'
              value: signalR.listKeys().primaryConnectionString
          }
          {
              name: 'AzureSignalRServiceTransportType'
              value: 'Transient'
          }
          {
              name: 'ONEFUZZ_INSTANCE_NAME'
              value: name
          }
          {
              name: 'ONEFUZZ_INSTANCE'
              value: 'https://${name}.azurewebsites.net'
          }
          {
              name: 'ONEFUZZ_RESOURCE_GROUP'
              value: resourceGroup().id
          }
          {
              name: 'ONEFUZZ_DATA_STORAGE'
              value: storageAccounts[storageAccountIndex].id
          }
          {
              name: 'ONEFUZZ_FUNC_STORAGE'
              value: storageAccounts[storageAccountFuncIndex].id
          }
          {
              name: 'ONEFUZZ_MONITOR'
              value: monitorAccountName
          }
          {
              name: 'ONEFUZZ_KEYVAULT'
              value: keyVaultName
          }
          {
              name: 'ONEFUZZ_OWNER'
              value: owner
          }
          {
              name: 'ONEFUZZ_CLIENT_SECRET'
              value: clientSecret
          }
      ]
      linuxFxVersion: 'Python|3.8'
      alwaysOn: true
      defaultDocuments: []
      httpLoggingEnabled: true
      logsDirectorySizeLimit: 100
      detailedErrorLoggingEnabled: true
      http20Enabled: true
      ftpsState: 'Disabled'
    }
    httpsOnly: true
    serverFarmId: serverFarms.id
    clientAffinityEnabled: true
  }
}

output fuzz_storage string = storageAccounts[storageAccountIndex].id
output fuzz_name string = storageAccountName
output fuzz_key string = storageAccounts[storageAccountIndex].listKeys().keys[0].value

output func_storage string = storageAccounts[storageAccountFuncIndex].id
output func_name string = storageAccountNameFunc
output func_key string = storageAccounts[storageAccountFuncIndex].listKeys().keys[0].value

output scaleset_identity string = scaleset_identity
output tenant_id string = tenantId
