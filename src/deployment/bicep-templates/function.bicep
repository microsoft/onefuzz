param name string
param location string
param owner string

param server_farm_id string
param client_id string
param app_func_issuer string
param app_func_audiences array
param use_windows bool
param enable_remote_debugging bool

param logs_storage string
param signedExpiry string


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

var siteconfig = (use_windows) ? {
} : {
  linuxFxVersion: linux_fx_version
}

var storage_account_sas = {
  signedExpiry: signedExpiry
  signedPermission: 'rwdlacup'
  signedResourceTypes: 'sco'
  signedServices: 'bfqt'
}


var commonSiteConfig = {
  alwaysOn: true
  defaultDocuments: []
  httpLoggingEnabled: true
  logsDirectorySizeLimit: 100
  detailedErrorLoggingEnabled: true
  http20Enabled: true
  ftpsState: 'Disabled'
  use32BitWorkerProcess: false
}

var extraProperties = (use_windows && enable_remote_debugging) ? {
  netFrameworkVersion: 'v7.0'
  remoteDebuggingEnabled: true
  remoteDebuggingVersion: 'VS2022'
} : {}

resource funcStorage 'Microsoft.Storage/storageAccounts@2021-08-01' existing = {
  name: logs_storage
}


resource function 'Microsoft.Web/sites@2021-03-01' = {
  name: name
  location: location
  kind: (use_windows) ? 'functionapp' : 'functionapp,linux'
  tags: {
    OWNER: owner
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: union({
      siteConfig: union(siteconfig, commonSiteConfig)
      httpsOnly: true
      serverFarmId: server_farm_id
      clientAffinityEnabled: true
    }, extraProperties)
}

resource funcAuthSettings 'Microsoft.Web/sites/config@2021-03-01' = {
  name: 'authsettingsV2'
  properties: {
    login: {
      tokenStore: {
        enabled: true
      }
    }
    globalValidation: {
      unauthenticatedClientAction: 'RedirectToLoginPage'
      requireAuthentication: true
      excludedPaths: [ '/api/config' ]
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

var sas = funcStorage.listAccountSas('2021-08-01', storage_account_sas)
resource funcLogs 'Microsoft.Web/sites/config@2021-03-01' = {
  name: 'logs'
  properties: {
    applicationLogs: {
      azureBlobStorage: {
        level: diagnostics_log_level
        retentionInDays: log_retention
        sasUrl: '${funcStorage.properties.primaryEndpoints.blob}app-logs?${sas.accountSasToken}'
      }
    }
  }
  parent: function
}

output principalId string = reference(function.id, function.apiVersion, 'Full').identity.principalId
