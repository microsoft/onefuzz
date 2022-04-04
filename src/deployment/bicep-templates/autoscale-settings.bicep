param location string
param server_farm_id string
param owner string

var autoscale_name = 'onefuzz-autoscale-${uniqueString(resourceGroup().id)}' 

resource autoscaleSettings 'Microsoft.Insights/autoscalesettings@2015-04-01' = {
  name: autoscale_name
  location: location 
  properties: {
    name: autoscale_name
    enabled: true
    targetResourceUri: server_farm_id
    targetResourceLocation: location
    notifications: []
    profiles:[
      {
        name: 'Auto scale condition'
        capacity: {
          default: '1'
          maximum: '20'
          minimum: '1'
        }
        rules: [
          {
            metricTrigger: {
              metricName: 'CpuPercentage' 
              metricResourceUri: server_farm_id
              operator: 'GreaterThanOrEqual'
              statistic: 'Average'
              threshold: 20
              timeAggregation: 'Average'
              timeGrain: 'PT1M'
              timeWindow: 'PT1M'
            }
            scaleAction: {
              cooldown: 'PT1M'
              direction: 'Increase'
              type: 'ChangeCount'
              value: '5'
            }
          }
          {
            metricTrigger: {
              metricName: 'CpuPercentage'
              metricResourceUri: server_farm_id
              operator: 'LessThan'
              statistic: 'Average'
              threshold: 20
              timeAggregation:'Average' 
              timeGrain: 'PT1M'
              timeWindow: 'PT1M'
            }
            scaleAction: {
              cooldown: 'PT5M'
              direction: 'Decrease'
              type: 'ChangeCount'
              value: '1'
            }
          }

        ]
      }
    ]
  }
  tags: {
    OWNER: owner
  }
}
