using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Azure.Storage.Queues.Models;
using System.Linq;

namespace Microsoft.OneFuzz.Service;

public class QueueFileChanges {
    // The number of time the function will be retried if an error occurs
    // https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-queue-trigger?tabs=csharp#poison-messages
    const int MAX_DEQUEUE_COUNT = 5;

    private readonly ILogger _logger;
    private readonly IStorageProvider _storageProvider;

    private readonly IStorage _storage;

    public QueueFileChanges(ILoggerFactory loggerFactory, IStorageProvider storageProvider, IStorage storage)
    {
        _logger = loggerFactory.CreateLogger<QueueFileChanges>();
        _storageProvider = storageProvider;
        _storage = storage;
    }

    [Function("QueueFileChanges")]
    public Task Run(
        [QueueTrigger("file-changes-refactored", Connection = "AzureWebJobsStorage")] string msg,
        int dequeueCount)
    {
        var fileChangeEvent = JsonSerializer.Deserialize<Dictionary<string, string>>(msg, EntityConverter.GetJsonSerializerOptions());        
        var lastTry = dequeueCount == MAX_DEQUEUE_COUNT;

        var _ = fileChangeEvent ?? throw new ArgumentException("Unable to parse queue trigger as JSON");

        // check type first before calling Azure APIs
        const string eventType = "eventType";
        if (!fileChangeEvent.ContainsKey(eventType)
            || fileChangeEvent[eventType] != "Microsoft.Storage.BlobCreated")
        {
            return Task.CompletedTask;
        }

        const string topic = "topic";
        if (!fileChangeEvent.ContainsKey(topic)
            || !_storage.CorpusAccounts().Contains(fileChangeEvent[topic]))
        {
            return Task.CompletedTask;
        }

        file_added(fileChangeEvent, lastTry);
        return Task.CompletedTask;
    }

    private void file_added(Dictionary<string, string> fileChangeEvent, bool failTaskOnTransientError) {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(fileChangeEvent["data"])!;
        var url = data["url"];
        var parts = url.Split("/").Skip(3).ToList();

        var container = parts[0];
        var path = string.Join('/', parts.Skip(1));

        _logger.LogInformation($"file added container: {container} - path: {path}");
        // TODO: new_files(container, path, fail_task_on_transient_error)
    }
}
