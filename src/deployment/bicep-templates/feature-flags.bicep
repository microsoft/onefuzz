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

// Gets the primary readonly connection string
// 0 - Primary Connection String
// 1 - Secondary Connection String
// 2 - Primary Readonly Connection String
// 3 - Secondary Readonly Connection String
var AppConfigurationConnectionString = featureFlags.listKeys().value[2].connectionString
output AppConfigurationConnectionString string = AppConfigurationConnectionString
