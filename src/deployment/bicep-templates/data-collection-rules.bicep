// https://learn.microsoft.com/en-us/azure/templates/microsoft.insights/datacollectionrules?pivots=deployment-language-bicep

param location string
param logAnalyticsWorkspaceResourceId string

var logAnalyticsWorkspaceDestination = 'logAnalyticsWorkspaceDestination'
var microsoftEventStream = 'Microsoft-Event'
var microsoftSyslogStream = 'Microsoft-Syslog'

resource scalesetDataCollectionRule 'Microsoft.Insights/dataCollectionRules@2021-09-01-preview' = {
  name: 'scalesetDataCollectionRule'
  location: location
  properties: {
    description: 'Collects data from scaleset VMs'
    dataSources: {
      windowsEventLogs: [
        {
          name: 'eventsLogDataSource'
          streams: [ microsoftEventStream ]
          xPathQueries: [
            'Application!*[System[(Level=1 or Level=2 or Level=3 or Level=4 or Level=0 or Level=5)]]'
            'Security!*[System[(band(Keywords,4503599627370496))]]'
            'System!*[System[(Level=1 or Level=2 or Level=3 or Level=4 or Level=0 or Level=5)]]'
          ]
        }
      ]
      syslog: [
        {
          name: 'sysLogsDataSource'
          facilityNames: [
            'auth'
            'authpriv'
            'cron'
            'daemon'
            'mark'
            'kern'
            'local0'
            'local1'
            'local2'
            'local3'
            'local4'
            'local5'
            'local6'
            'local7'
            'lpr'
            'mail'
            'news'
            'syslog'
            'user'
            'uucp'
          ]
          logLevels: [
            'Debug'
            'Info'
            'Notice'
            'Warning'
            'Error'
            'Critical'
            'Alert'
            'Emergency'
          ]
          streams: [ microsoftSyslogStream ]
        }
      ]
    }
    destinations: {
      logAnalytics: [
        {
          name: logAnalyticsWorkspaceDestination
          workspaceResourceId: logAnalyticsWorkspaceResourceId
        }
      ]
    }
    dataFlows: [
      {
        streams: [ microsoftEventStream, microsoftSyslogStream ]
        destinations: [ logAnalyticsWorkspaceDestination ]
      }
    ]
  }
}
