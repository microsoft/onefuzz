param location string

var signalr_name = 'onefuzz-${uniqueString(resourceGroup().id)}'
resource signalR 'Microsoft.SignalRService/signalR@2021-10-01' = {
  name: signalr_name
  location: location
  sku: {
    name: 'Standard_S1'
    tier: 'Standard'
    capacity: 1
  }
  properties: {
    features: [
      {
          flag: 'ServiceMode'
          value: 'Serverless'
          properties: {}
      }
      {
          flag: 'EnableConnectivityLogs'
          value: 'True'
          properties: {}
      }
      {
          flag: 'EnableMessagingLogs'
          value: 'False'
          properties: {}
      }
    ]
  }
}

var connectionString = signalR.listKeys().primaryConnectionString
output connectionString string = connectionString
