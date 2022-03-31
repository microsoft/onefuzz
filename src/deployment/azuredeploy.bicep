param name string
param owner string
param clientId string
param clientSecret string
param signedExpiry string
param app_func_issuer string
param app_func_audiences array
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

var log_retention = 30
var tenantId = subscription().tenantId

var scaleset_identity = '${name}-scalesetid'

var telemetry = 'd7a73cf4-5a1a-4030-85e1-e5b25867e45a'
var StorageBlobDataReader = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'

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
resource scalesetIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2018-11-30' = {
  name: scaleset_identity
  location: location
}

module serverFarms 'bicep-templates/server-farms.bicep' = {
  name: 'server-farms' 
  params: {
    server_farm_name: name
    owner: owner
    location: location
  }
}

module keyVaults 'bicep-templates/keyvaults.bicep' = {
  name: 'keyvaults'
  params: {
    location: location
    principal_id: reference(pythonFunction.id, pythonFunction.apiVersion, 'Full').identity.principalId
    tenant_id: tenantId
  }
}

module signalR 'bicep-templates/signalR.bicep' = {
  name: 'signalR'
  params: {
    location: location
  }
}

module storage 'bicep-templates/storageAccounts.bicep' = {
  name: 'storage'
  params: {
    location: location
    owner: owner
    signedExpiry: signedExpiry
  }
}

module autoscaleSettings 'bicep-templates/autoscale-settings.bicep' = {
  name: 'autoscaleSettings'
  params: {
    location: location
    server_farm_id: serverFarms.outputs.id
    owner: owner
  }
}

module operationalInsights 'bicep-templates/operational-insights.bicep' = {
  name: 'operational-insights'
  params: {
    name: name
    location: location
    log_retention: log_retention
    owner: owner
    workbookData: workbookData
  }
}

module eventGrid 'bicep-templates/event-grid.bicep' = {
  name: 'event-grid'
  params:{
    location: location
    storageFuzzId: storage.outputs.FuzzId
    storageFuncId: storage.outputs.FuncId
    fileChangesQueueName: storage.outputs.FileChangesQueueName
  }
  dependsOn: [
    storage
  ]
}

// try to make role assignments to deploy as late as possible in order to has principalId ready
resource roleAssigments 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = [for r in roleAssignmentsParams: {
  name: guid('${resourceGroup().id}${r.suffix}')
  properties: {
    roleDefinitionId: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/${r.role}'
    principalId: reference(pythonFunction.id, pythonFunction.apiVersion, 'Full').identity.principalId
  }
  dependsOn: [
    eventGrid
    keyVaults
    serverFarms
  ]
}]

// try to make role assignments to deploy as late as possible in order to has principalId ready
resource readBlobUserAssignment 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid('${resourceGroup().id}-user_managed_idenity_read_blob')
  properties: {
    roleDefinitionId: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/${StorageBlobDataReader}'
    principalId: reference(scalesetIdentity.id, scalesetIdentity.apiVersion, 'Full').properties.principalId
  }
  dependsOn: [
    eventGrid
    keyVaults
    serverFarms
  ]
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
    serverFarmId: serverFarms.outputs.id
    clientAffinityEnabled: true
  }
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
          allowedAudiences: app_func_audiences
        }
      }
    }
  }
  parent: pythonFunction
}

resource funcLogs 'Microsoft.Web/sites/config@2021-03-01' = {
  name: 'logs'
  properties: {
    applicationLogs: {
      azureBlobStorage: {
        level: diagnosticsLogLevel
        retentionInDays: log_retention
        sasUrl: storage.outputs.FuncSasUrlBlobAppLogs
      }
    }
  }
  parent: pythonFunction
}

resource pythonFunctionSettings 'Microsoft.Web/sites/config@2021-03-01' = {
  name: 'appsettings'
  parent: pythonFunction
  properties: {
      'FUNCTIONS_EXTENSION_VERSION': '~3'
      'FUNCTIONS_WORKER_RUNTIME': 'python'
      'FUNCTIONS_WORKER_PROCESS_COUNT': '1'
      'APPINSIGHTS_INSTRUMENTATIONKEY': operationalInsights.outputs.appInsightsInstrumentationKey
      'APPINSIGHTS_APPID': operationalInsights.outputs.appInsightsAppId
      'ONEFUZZ_TELEMETRY': telemetry
      'AzureWebJobsStorage': storage.outputs.FuncSasUrl
      'MULTI_TENANT_DOMAIN': multi_tenant_domain
      'AzureWebJobsDisableHomepage': 'true'
      'AzureSignalRConnectionString': signalR.outputs.connectionString
      'AzureSignalRServiceTransportType': 'Transient'
      'ONEFUZZ_INSTANCE_NAME': name
      'ONEFUZZ_INSTANCE': 'https://${name}.azurewebsites.net'
      'ONEFUZZ_RESOURCE_GROUP': resourceGroup().id
      'ONEFUZZ_DATA_STORAGE': storage.outputs.FuzzId
      'ONEFUZZ_FUNC_STORAGE': storage.outputs.FuncId
      'ONEFUZZ_MONITOR': operationalInsights.outputs.monitorAccountName
      'ONEFUZZ_KEYVAULT': keyVaults.outputs.name
      'ONEFUZZ_OWNER': owner
      'ONEFUZZ_CLIENT_SECRET': clientSecret
  }
}

output fuzz_storage string = storage.outputs.FuzzId
output fuzz_name string = storage.outputs.FuzzName
output fuzz_key string = storage.outputs.FuzzKey

output func_storage string = storage.outputs.FuncId
output func_name string = storage.outputs.FuncName
output func_key string = storage.outputs.FuncKey

output scaleset_identity string = scaleset_identity
output tenant_id string = tenantId
