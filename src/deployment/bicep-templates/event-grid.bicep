param location string
param storageFuzzId string
param storageFuncId string
param fileChangesQueueName string

var suffix = uniqueString(resourceGroup().id)
var fuzz_blob_topic_name ='fuzz-blob-topic-${suffix}'

resource eventGridSystemTopics 'Microsoft.EventGrid/systemTopics@2021-12-01' = {
  name: fuzz_blob_topic_name
  location: location
  properties: {
    source: storageFuzzId
    topicType: 'microsoft.storage.storageaccounts'
  }
}

resource eventSubscriptions 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2021-12-01' = {
  name: 'onefuzz1_subscription'
  parent: eventGridSystemTopics
  properties: {
    destination: {
      properties: {
        resourceId: storageFuncId
        queueName: fileChangesQueueName
      }
      endpointType: 'StorageQueue'
    }
    filter: {
      includedEventTypes: [
        'Microsoft.Storage.BlobCreated'
        'Microsoft.Storage.BlobDeleted'
      ]
    }
    eventDeliverySchema: 'EventGridSchema'
    retryPolicy: {
      maxDeliveryAttempts: 30
      eventTimeToLiveInMinutes: 1440
    }
  }
}
