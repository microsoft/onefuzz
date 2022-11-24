param server_farm_name string
param owner string
param location string
param use_windows bool

var kind = (use_windows) ? 'app' : 'linux'

resource serverFarms 'Microsoft.Web/serverfarms@2022-03-01' = {
  name: server_farm_name
  location: location
  kind: kind
  properties: {
    // reserved must be set to true for Linux server farm, otherwise it is false
    reserved: !use_windows
  }
  sku: {
    name: 'P2v2'
    tier: 'PremiumV2'
    family: 'Pv2'
    capacity: 1
  }
  tags: {
    OWNER: owner
  }
}

output id string = serverFarms.id
output kind string = kind
