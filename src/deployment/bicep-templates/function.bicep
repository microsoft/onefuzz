param name string
param instance_name string
param location string
param owner string

param server_farm_id string
param client_id string
param app_func_issuer string
param app_func_audiences array

@secure()
param app_logs_sas_url string

param app_insights_app_id string
@secure()
param app_insights_key string

@secure()
param func_sas_url string

param multi_tenant_domain string

@secure()
param signal_r_connection_string string

@description('The degree of severity for diagnostics logs.')
@allowed([
  'Verbose'
  'Information'
  'Warning'
  'Error'
])
param diagnostics_log_level string
param log_retention int

param func_storage_resource_id string
param fuzz_storage_resource_id string

param keyvault_name string

@secure()
param client_secret string

param monitor_account_name string

param linux_fx_version string
param functions_worker_runtime string
param functions_extension_version string

var telemetry = 'd7a73cf4-5a1a-4030-85e1-e5b25867e45a'

resource function 'Microsoft.Web/sites@2021-03-01' = {
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
      linuxFxVersion: linux_fx_version
      alwaysOn: true
      defaultDocuments: []
      httpLoggingEnabled: true
      logsDirectorySizeLimit: 100
      detailedErrorLoggingEnabled: true
      http20Enabled: true
      ftpsState: 'Disabled'
    }
    httpsOnly: true
    serverFarmId: server_farm_id
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
          clientId: client_id
          openIdIssuer: app_func_issuer
          clientSecretSettingName: 'ONEFUZZ_CLIENT_SECRET'
        }
        validation: {
          allowedAudiences: app_func_audiences
        }
      }
    }
  }
  parent: function
}

resource funcLogs 'Microsoft.Web/sites/config@2021-03-01' = {
  name: 'logs'
  properties: {
    applicationLogs: {
      azureBlobStorage: {
        level: diagnostics_log_level
        retentionInDays: log_retention
        sasUrl: app_logs_sas_url
      }
    }
  }
  parent: function
}

resource pythonFunctionSettings 'Microsoft.Web/sites/config@2021-03-01' = {
  name: 'appsettings'
  parent: function
  properties: {
      'FUNCTIONS_EXTENSION_VERSION': functions_extension_version
      'FUNCTIONS_WORKER_RUNTIME': functions_worker_runtime
      'FUNCTIONS_WORKER_PROCESS_COUNT': '1'
      'APPINSIGHTS_INSTRUMENTATIONKEY': app_insights_key
      'APPINSIGHTS_APPID': app_insights_app_id
      'ONEFUZZ_TELEMETRY': telemetry
      'AzureWebJobsStorage': func_sas_url
      'MULTI_TENANT_DOMAIN': multi_tenant_domain
      'AzureWebJobsDisableHomepage': 'true'
      'AzureSignalRConnectionString': signal_r_connection_string
      'AzureSignalRServiceTransportType': 'Transient'
      'ONEFUZZ_INSTANCE_NAME': instance_name
      'ONEFUZZ_INSTANCE': 'https://${name}.azurewebsites.net'
      'ONEFUZZ_RESOURCE_GROUP': resourceGroup().id
      'ONEFUZZ_DATA_STORAGE': fuzz_storage_resource_id
      'ONEFUZZ_FUNC_STORAGE': func_storage_resource_id
      'ONEFUZZ_MONITOR': monitor_account_name
      'ONEFUZZ_KEYVAULT': keyvault_name
      'ONEFUZZ_OWNER': owner
      'ONEFUZZ_CLIENT_SECRET': client_secret
  }
}

output principalId string = reference(function.id, function.apiVersion, 'Full').identity.principalId
