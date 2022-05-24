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

var python_functions_disabled = '0'
var dotnet_functions_disabled = '1'

var scaleset_identity = '${name}-scalesetid'

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
    accessPolicies: [
      {
        objectId: pythonFunction.outputs.principalId
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
      {
        objectId: netFunction.outputs.principalId
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
    tenantId: tenantId
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

// try to make role assignments to deploy as late as possible in order to have principalId ready
resource roleAssigmentsPy 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = [for r in roleAssignmentsParams: {
  name: guid('${resourceGroup().id}${r.suffix}-python')
  properties: {
    roleDefinitionId: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/${r.role}'
    principalId: pythonFunction.outputs.principalId
  }
  dependsOn: [
    eventGrid
    keyVault
    serverFarms
  ]
}]

// try to make role assignments to deploy as late as possible in order to have principalId ready
resource roleAssigmentsNet 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = [for r in roleAssignmentsParams: {
  name: guid('${resourceGroup().id}${r.suffix}-net')
  properties: {
    roleDefinitionId: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/${r.role}'
    principalId: netFunction.outputs.principalId
  }
  dependsOn: [
    eventGrid
    keyVault
    serverFarms
  ]
}]


// try to make role assignments to deploy as late as possible in order to have principalId ready
resource readBlobUserAssignment 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid('${resourceGroup().id}-user_managed_idenity_read_blob')
  properties: {
    roleDefinitionId: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/${StorageBlobDataReader}'
    principalId: reference(scalesetIdentity.id, scalesetIdentity.apiVersion, 'Full').properties.principalId
  }
  dependsOn: [
    eventGrid
    keyVault
    serverFarms
  ]
}


module pythonFunction 'bicep-templates/function.bicep' = {
  name: 'pythonFunction'
  params: {
    name: name
    linux_fx_version: 'Python|3.8'

    app_logs_sas_url: storage.outputs.FuncSasUrlBlobAppLogs
    app_func_audiences: app_func_audiences
    app_func_issuer: app_func_issuer

    diagnostics_log_level: diagnosticsLogLevel
    location: location
    log_retention: log_retention
    owner: owner
    server_farm_id: serverFarms.outputs.id
    client_id: clientId
  }
}

module netFunction 'bicep-templates/function.bicep' = {
  name: 'netFunction'
  params: {
    linux_fx_version: 'DOTNET-ISOLATED|6.0'
    name: '${name}-net'

    app_logs_sas_url: storage.outputs.FuncSasUrlBlobAppLogs
    app_func_audiences: app_func_audiences
    app_func_issuer: app_func_issuer
    client_id: clientId
    diagnostics_log_level: diagnosticsLogLevel
    location: location
    log_retention: log_retention
    owner: owner
    server_farm_id: serverFarms.outputs.id
  }
}

module pythonFunctionSettings 'bicep-templates/function-settings.bicep' = {
  name: 'pythonFunctionSettings'
  params: {
    name: name
    owner: owner
    functions_worker_runtime: 'python'
    functions_extension_version: '~3'
    instance_name: name
    app_insights_app_id: operationalInsights.outputs.appInsightsAppId
    app_insights_key: operationalInsights.outputs.appInsightsInstrumentationKey
    client_secret: clientSecret
    signal_r_connection_string: signalR.outputs.connectionString
    func_sas_url: storage.outputs.FuncSasUrl
    func_storage_resource_id: storage.outputs.FuncId
    fuzz_storage_resource_id: storage.outputs.FuzzId
    keyvault_name: keyVaultName
    monitor_account_name: operationalInsights.outputs.monitorAccountName
    multi_tenant_domain: multi_tenant_domain
    functions_disabled: python_functions_disabled
  }
  dependsOn: [
    pythonFunction
  ]
}


module netFunctionSettings 'bicep-templates/function-settings.bicep' = {
  name: 'netFunctionSettings'
  params: {
    owner: owner
    name: '${name}-net'
    signal_r_connection_string: signalR.outputs.connectionString
    app_insights_app_id: operationalInsights.outputs.appInsightsAppId
    app_insights_key: operationalInsights.outputs.appInsightsInstrumentationKey
    functions_worker_runtime: 'dotnet-isolated'
    functions_extension_version: '~4'
    instance_name: name
    client_secret: clientSecret
    func_sas_url: storage.outputs.FuncSasUrl
    func_storage_resource_id: storage.outputs.FuncId
    fuzz_storage_resource_id: storage.outputs.FuzzId
    keyvault_name: keyVaultName
    monitor_account_name: operationalInsights.outputs.monitorAccountName
    multi_tenant_domain: multi_tenant_domain
    functions_disabled: dotnet_functions_disabled
  }
  dependsOn: [
    netFunction
  ]
}


output fuzz_storage string = storage.outputs.FuzzId
output fuzz_name string = storage.outputs.FuzzName
output fuzz_key string = storage.outputs.FuzzKey

output func_storage string = storage.outputs.FuncId
output func_name string = storage.outputs.FuncName
output func_key string = storage.outputs.FuncKey

output scaleset_identity string = scaleset_identity
output tenant_id string = tenantId
