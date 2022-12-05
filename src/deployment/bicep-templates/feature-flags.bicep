param location string

var suffix = uniqueString(resourceGroup().id)
var appConfigName = 'app-config-${suffix}'

resource featureFlags 'Microsoft.AppConfiguration/configurationStores@2022-05-01' = {
  name: appConfigName
  location: location
  sku:{
    name: 'standard'
  }
}

output AppConfigEndpoint string = 'https://${appConfigName}.azconfig.io'
