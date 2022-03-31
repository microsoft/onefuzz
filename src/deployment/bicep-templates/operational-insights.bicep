param name string
param location string
param log_retention int
param owner string
param workbookData object

var monitorAccountName = name

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

var onefuzz = {
  severitiesAtMostInfo: [
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
  location: location
  dependsOn: [
    insightsMonitorAccount
  ]
  properties: {
    workspaceResourceId: resourceId('Microsoft.OperationalInsights/workspaces', monitorAccountName) 
  }
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
    WorkspaceResourceId: insightsMonitorAccount.id
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

output monitorAccountName string = monitorAccountName
output appInsightsAppId string = insightsComponents.properties.AppId
output appInsightsInstrumentationKey string = insightsComponents.properties.InstrumentationKey
