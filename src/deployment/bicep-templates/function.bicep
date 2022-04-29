param name string
param location string
param owner string

param server_farm_id string
param client_id string
param app_func_issuer string
param app_func_audiences array

@secure()
param app_logs_sas_url string

@description('The degree of severity for diagnostics logs.')
@allowed([
  'Verbose'
  'Information'
  'Warning'
  'Error'
])
param diagnostics_log_level string
param log_retention int
param linux_fx_version string


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

output principalId string = reference(function.id, function.apiVersion, 'Full').identity.principalId
