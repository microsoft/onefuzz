param name string
param instance_name string
param owner string
param app_insights_app_id string
@secure()
param app_insights_key string

@secure()
param func_sas_url string

param multi_tenant_domain string

@secure()
param signal_r_connection_string string

param func_storage_resource_id string
param fuzz_storage_resource_id string

param keyvault_name string

@secure()
param client_secret string

param monitor_account_name string

param functions_worker_runtime string
param functions_extension_version string

param functions_disabled string

var disabledFunctionName = 'disabledFunctions-${functions_worker_runtime}'

var telemetry = 'd7a73cf4-5a1a-4030-85e1-e5b25867e45a'

resource function 'Microsoft.Web/sites@2021-02-01' existing = {
  name: name
}

module disabledFunctions 'function-settings-disabled-apps.bicep' = {
  name: disabledFunctionName
  params:{
    functions_disabled_setting: functions_disabled
  }
}

resource functionSettings 'Microsoft.Web/sites/config@2021-03-01' = {
  parent: function
  name: 'appsettings'
  properties: union({
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
  }, disabledFunctions.outputs.appSettings)
}

