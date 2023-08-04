// https://learn.microsoft.com/en-us/azure/templates/microsoft.insights/datacollectionrules?pivots=deployment-language-bicep

param location string
param logAnalyticsWorkspaceResourceId string

var logAnalyticsWorkspaceDestination = 'logAnalyticsWorkspaceDestination'
var microsoftEventStream = 'Microsoft-Event'
var microsoftSyslogStream = 'Microsoft-Syslog'
var microsoftPerfStream = 'Microsoft-Perf'

resource scalesetDataCollectionEndpoint 'Microsoft.Insights/dataCollectionEndpoints@2021-09-01-preview' = {
  name: 'scalesetDataCollectionEndpoint'
  location: location
}

resource scalesetDataCollectionRule 'Microsoft.Insights/dataCollectionRules@2021-09-01-preview' = {
  dependsOn: [ scalesetDataCollectionEndpoint ]
  name: 'scalesetDataCollectionRule'
  location: location
  properties: {
    description: 'Collects data from scaleset VMs'
    dataCollectionEndpointId: scalesetDataCollectionEndpoint.id
    streamDeclarations: {
      'Custom-MyTable_CL': {
        'columns': [
          {
            'name': 'TimeGenerated'
            'type': 'datetime'
          }
          {
            'name': 'RawData'
            'type': 'string'
          }
        ]
      }
    }
    dataSources: {
      performanceCounters: [
        {
          streams: [ microsoftPerfStream ]
          samplingFrequencyInSeconds: 60
          counterSpecifiers: [
            '\\Processor Information(_Total)\\% Processor Time'
            '\\Processor Information(_Total)\\% Privileged Time'
            '\\Processor Information(_Total)\\% User Time'
            '\\Processor Information(_Total)\\Processor Frequency'
            '\\System\\Processes'
            '\\Process(_Total)\\Thread Count'
            '\\Process(_Total)\\Handle Count'
            '\\System\\System Up Time'
            '\\System\\Context Switches/sec'
            '\\System\\Processor Queue Length'
            '\\Memory\\% Committed Bytes In Use'
            '\\Memory\\Available Bytes'
            '\\Memory\\Committed Bytes'
            '\\Memory\\Cache Bytes'
            '\\Memory\\Pool Paged Bytes'
            '\\Memory\\Pool Nonpaged Bytes'
            '\\Memory\\Pages/sec'
            '\\Memory\\Page Faults/sec'
            '\\Process(_Total)\\Working Set'
            '\\Process(_Total)\\Working Set - Private'
            '\\LogicalDisk(_Total)\\% Disk Time'
            '\\LogicalDisk(_Total)\\% Disk Read Time'
            '\\LogicalDisk(_Total)\\% Disk Write Time'
            '\\LogicalDisk(_Total)\\% Idle Time'
            '\\LogicalDisk(_Total)\\Disk Bytes/sec'
            '\\LogicalDisk(_Total)\\Disk Read Bytes/sec'
            '\\LogicalDisk(_Total)\\Disk Write Bytes/sec'
            '\\LogicalDisk(_Total)\\Disk Transfers/sec'
            '\\LogicalDisk(_Total)\\Disk Reads/sec'
            '\\LogicalDisk(_Total)\\Disk Writes/sec'
            '\\LogicalDisk(_Total)\\Avg. Disk sec/Transfer'
            '\\LogicalDisk(_Total)\\Avg. Disk sec/Read'
            '\\LogicalDisk(_Total)\\Avg. Disk sec/Write'
            '\\LogicalDisk(_Total)\\Avg. Disk Queue Length'
            '\\LogicalDisk(_Total)\\Avg. Disk Read Queue Length'
            '\\LogicalDisk(_Total)\\Avg. Disk Write Queue Length'
            '\\LogicalDisk(_Total)\\% Free Space'
            '\\LogicalDisk(_Total)\\Free Megabytes'
            '\\Network Interface(*)\\Bytes Total/sec'
            '\\Network Interface(*)\\Bytes Sent/sec'
            '\\Network Interface(*)\\Bytes Received/sec'
            '\\Network Interface(*)\\Packets/sec'
            '\\Network Interface(*)\\Packets Sent/sec'
            '\\Network Interface(*)\\Packets Received/sec'
            '\\Network Interface(*)\\Packets Outbound Errors'
            '\\Network Interface(*)\\Packets Received Errors'
            'Processor(*)\\% Processor Time'
            'Processor(*)\\% Idle Time'
            'Processor(*)\\% User Time'
            'Processor(*)\\% Nice Time'
            'Processor(*)\\% Privileged Time'
            'Processor(*)\\% IO Wait Time'
            'Processor(*)\\% Interrupt Time'
            'Processor(*)\\% DPC Time'
            'Memory(*)\\Available MBytes Memory'
            'Memory(*)\\% Available Memory'
            'Memory(*)\\Used Memory MBytes'
            'Memory(*)\\% Used Memory'
            'Memory(*)\\Pages/sec'
            'Memory(*)\\Page Reads/sec'
            'Memory(*)\\Page Writes/sec'
            'Memory(*)\\Available MBytes Swap'
            'Memory(*)\\% Available Swap Space'
            'Memory(*)\\Used MBytes Swap Space'
            'Memory(*)\\% Used Swap Space'
            'Logical Disk(*)\\% Free Inodes'
            'Logical Disk(*)\\% Used Inodes'
            'Logical Disk(*)\\Free Megabytes'
            'Logical Disk(*)\\% Free Space'
            'Logical Disk(*)\\% Used Space'
            'Logical Disk(*)\\Logical Disk Bytes/sec'
            'Logical Disk(*)\\Disk Read Bytes/sec'
            'Logical Disk(*)\\Disk Write Bytes/sec'
            'Logical Disk(*)\\Disk Transfers/sec'
            'Logical Disk(*)\\Disk Reads/sec'
            'Logical Disk(*)\\Disk Writes/sec'
            'Network(*)\\Total Bytes Transmitted'
            'Network(*)\\Total Bytes Received'
            'Network(*)\\Total Bytes'
            'Network(*)\\Total Packets Transmitted'
            'Network(*)\\Total Packets Received'
            'Network(*)\\Total Rx Errors'
            'Network(*)\\Total Tx Errors'
            'Network(*)\\Total Collisions'
            'System(*)\\Uptime'
            'System(*)\\Load1'
            'System(*)\\Load5'
            'System(*)\\Load15'
            'System(*)\\Users'
            'System(*)\\Unique Users'
            'System(*)\\CPUs'
          ]
          name: 'perfCounterDataSource60'
        }
      ]
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
        streams: [ microsoftPerfStream ]
        destinations: [ logAnalyticsWorkspaceDestination ]
        transformKql: 'source'
        outputStream: microsoftPerfStream
      }
      {
        streams: [ microsoftEventStream ]
        destinations: [ logAnalyticsWorkspaceDestination ]
        transformKql: 'source'
        outputStream: microsoftEventStream
      }
      {
        streams: [ microsoftSyslogStream ]
        destinations: [ logAnalyticsWorkspaceDestination ]
        transformKql: 'source'
        outputStream: microsoftSyslogStream
      }
    ]
  }
}
