param server_farm_name string
param owner string
param location string

resource serverFarms 'Microsoft.Web/serverfarms@2021-03-01' = {
  name: server_farm_name
  location: location
  kind: 'linux'
  properties: {
    reserved: true
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
