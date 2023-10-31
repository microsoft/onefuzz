param name string
param owner string
param clientId string

@secure()
param clientSecret string

param signedExpiry string
param app_func_issuer string
param app_func_audiences array
param cli_app_id string
param authority string
param tenant_domain string
param multi_tenant_domain string
param enable_remote_debugging bool = false
param enable_profiler bool = false

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

var StorageBlobDataReader = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'

var roleAssignmentsParams = [
  {
    suffix: '-vmss'
    role: '9980e02c-c2be-4d73-94e8-173b1dc7cf3c' //VirtualMachineContributor
  }
  {
    suffix: '-storage'
    role: '17d1049b-9a84-46fb-8f53-869881c3d3ab' //StorageAccountContributor
  }
  {
    suffix: '-network'
    role: '4d97b98b-1d4f-4787-a291-c67834d212e7' //NetworkContributor
  }
  {
    suffix: '-logs'
    role: '92aaf0da-9dab-42b6-94a3-d43ce8d16293' //LogAnalyticsContributor
  }
  {
    suffix: '-user_managed_identity'
    role: 'f1a07417-d97a-45cb-824c-7a7467783830' //ManagedIdentityOperator
  }
  {
    suffix: '-contributor'
    role: 'b24988ac-6180-42a0-ab88-20f7382dd24c' //Contributor
  }
  {
    suffix: '-app_config_reader'
    role: '516239f1-63e1-4d78-a4de-a74fb236a071' //App Configuration Data Reader
  }
]
resource scalesetIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2018-11-30' = {
  name: scaleset_identity
  location: location
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

module serverFarm 'bicep-templates/server-farms.bicep' = {
  name: 'server-farm'
  params: {
    server_farm_name: name
    owner: owner
    location: location
    use_windows: true
  }
}

var keyVaultName = 'of-kv-${uniqueString(resourceGroup().id)}'
resource keyVault 'Microsoft.KeyVault/vaults@2021-10-01' = {
  name: keyVaultName
  location: location
  properties: {
    enabledForDiskEncryption: false
    enabledForDeployment: true
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
        objectId: function.outputs.principalId
        tenantId: tenantId
        permissions: {
          secrets: [
            'get'
            'list'
            'set'
            'delete'
          ]
          certificates: [
            'get'
            'list'
          ]
        }
      }
      {
        objectId: 'b453993d-81d4-41a7-be3a-549bc2435ffa'
        tenantId: tenantId
        permissions: {
          secrets: [
            'get'
            'list'
          ]
          certificates: [
            'get'
            'list'
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
  }
}

module autoscaleSettings 'bicep-templates/autoscale-settings.bicep' = {
  name: 'autoscaleSettings'
  params: {
    location: location
    server_farm_id: serverFarm.outputs.id
    owner: owner
    workspaceId: operationalInsights.outputs.workspaceId
    autoscale_name: 'onefuzz-autoscale-${uniqueString(resourceGroup().id)}'
    function_diagnostics_settings_name: 'functionDiagnosticSettings'
  }
}


module eventGrid 'bicep-templates/event-grid.bicep' = {
  name: 'event-grid'
  params: {
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
resource roleAssignments 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = [for r in roleAssignmentsParams: {
  name: guid('${resourceGroup().id}${r.suffix}-1f')
  properties: {
    roleDefinitionId: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/${r.role}'
    principalId: function.outputs.principalId
  }
  dependsOn: [
    eventGrid
    keyVault
    serverFarm
    featureFlags
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
    serverFarm
    featureFlags
  ]
}

module featureFlags 'bicep-templates/feature-flags.bicep' = {
  name: 'featureFlags'
  params: {
    location: location
  }
}

module function 'bicep-templates/function.bicep' = {
  name: 'function'
  params: {
    name: name
    linux_fx_version: 'DOTNET-ISOLATED|7.0'
    signedExpiry: signedExpiry
    logs_storage: storage.outputs.FuncName
    app_func_audiences: app_func_audiences
    app_func_issuer: app_func_issuer
    client_id: clientId
    diagnostics_log_level: diagnosticsLogLevel
    location: location
    log_retention: log_retention
    owner: owner
    server_farm_id: serverFarm.outputs.id

    use_windows: true
    enable_remote_debugging: enable_remote_debugging
  }
  dependsOn:[
    storage
  ]
}

module functionSettings 'bicep-templates/function-settings.bicep' = {
  name: 'functionSettings'
  params: {
    name: name
    owner: owner
    functions_worker_runtime: 'dotnet-isolated'
    functions_extension_version: '~4'
    instance_name: name
    app_insights_app_id: operationalInsights.outputs.appInsightsAppId
    app_insights_key: operationalInsights.outputs.appInsightsInstrumentationKey
    client_secret: clientSecret

    signalRName: signalR.outputs.signalRName
    funcStorageName: storage.outputs.FuncName
    func_storage_resource_id: storage.outputs.FuncId
    fuzz_storage_resource_id: storage.outputs.FuzzId
    keyvault_name: keyVaultName
    monitor_account_name: operationalInsights.outputs.monitorAccountName
    cli_app_id: cli_app_id
    authority: authority
    tenant_domain: tenant_domain
    multi_tenant_domain: multi_tenant_domain
    enable_profiler: enable_profiler
    app_config_endpoint: featureFlags.outputs.AppConfigEndpoint
  }
  dependsOn: [
    function
    storage
    signalR
  ]
}

output fuzz_storage string = storage.outputs.FuzzId
output fuzz_name string = storage.outputs.FuzzName


output func_storage string = storage.outputs.FuncId
output func_name string = storage.outputs.FuncName


output scaleset_identity string = scaleset_identity
output tenant_id string = tenantId

output enable_remote_debugging bool = enable_remote_debugging
