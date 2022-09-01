﻿using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service.Functions;

public class QueueFileChanges {
    // The number of time the function will be retried if an error occurs
    // https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-queue-trigger?tabs=csharp#poison-messages
    const int MAX_DEQUEUE_COUNT = 5;

    private readonly ILogTracer _log;

    private readonly IStorage _storage;

    private readonly INotificationOperations _notificationOperations;

    public QueueFileChanges(ILogTracer log, IStorage storage, INotificationOperations notificationOperations) {
        _log = log;
        _storage = storage;
        _notificationOperations = notificationOperations;
    }

    //[Function("QueueFileChanges")]
    public async Async.Task Run(
        [QueueTrigger("file-changes-refactored", Connection = "AzureWebJobsStorage")] string msg,
        int dequeueCount) {
        var fileChangeEvent = JsonSerializer.Deserialize<JsonDocument>(msg, EntityConverter.GetJsonSerializerOptions());
        var lastTry = dequeueCount == MAX_DEQUEUE_COUNT;

        var _ = fileChangeEvent ?? throw new ArgumentException("Unable to parse queue trigger as JSON");

        // check type first before calling Azure APIs
        const string eventType = "eventType";
        if (!fileChangeEvent.RootElement.TryGetProperty(eventType, out var eventTypeElement)
            || eventTypeElement.GetString() != "Microsoft.Storage.BlobCreated") {
            return;
        }

        const string topic = "topic";
        if (!fileChangeEvent.RootElement.TryGetProperty(topic, out var topicElement)
            || !_storage.CorpusAccounts().Contains(topicElement.GetString())) {
            return;
        }

        await FileAdded(_log, fileChangeEvent, lastTry);
    }

    private async Async.Task FileAdded(ILogTracer log, JsonDocument fileChangeEvent, bool failTaskOnTransientError) {
        var data = fileChangeEvent.RootElement.GetProperty("data");
        var url = data.GetProperty("url").GetString()!;
        var parts = url.Split("/").Skip(3).ToList();

        var container = parts[0];
        var path = string.Join('/', parts.Skip(1));

        log.Info($"file added container: {container} - path: {path}");
        await _notificationOperations.NewFiles(new Container(container), path, failTaskOnTransientError);
    }
}
