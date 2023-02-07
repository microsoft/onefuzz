param location string

var suffix = uniqueString(resourceGroup().id)
var appConfigName = 'app-config-${suffix}'

resource featureFlags 'Microsoft.AppConfiguration/configurationStores@2022-05-01' = {
  name: appConfigName
  location: location
  sku: {
    name: 'standard'
  }
}

resource configStoreFeatureflag 'Microsoft.AppConfiguration/configurationStores/keyValues@2021-10-01-preview' = {
  parent: featureFlags
  name: '.appconfig.featureflag~2FEnableScribanOnly'
  properties: {
    value: string({
      id: 'EnableScribanOnly'
      description: 'Your description.'
      enabled: true
    })
    contentType: 'application/vnd.microsoft.appconfig.ff+json;charset=utf-8'
  }
}

output AppConfigEndpoint string = 'https://${appConfigName}.azconfig.io'
