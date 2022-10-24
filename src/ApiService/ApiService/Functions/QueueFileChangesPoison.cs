using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service.Functions;

public class QueueFileChangesPoison {
    // The number of time the function will be retried if an error occurs
    // https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-queue-trigger?tabs=csharp#poison-messages
    const int MAX_DEQUEUE_COUNT = 5;

    private const string queueFileChangesPoisonQueueName = "file-changes-poison";

    private readonly ILogTracer _log;

    private readonly IStorage _storage;

    private readonly INotificationOperations _notificationOperations;

    private readonly IOnefuzzContext _context;

    public QueueFileChangesPoison(ILogTracer log, IStorage storage, INotificationOperations notificationOperations, IOnefuzzContext context) {
        _log = log;
        _storage = storage;
        _notificationOperations = notificationOperations;
        _context = context;
    }

    [Function("QueueFileChangesPoison")]
    public async Async.Task Run(
        [QueueTrigger(queueFileChangesPoisonQueueName, Connection = "AzureWebJobsStorage")] string msg) {
        var fileChangeEvent = JsonSerializer.Deserialize<JsonDocument>(msg, EntityConverter.GetJsonSerializerOptions());

        _ = fileChangeEvent ?? throw new ArgumentException("Unable to parse queue trigger as JSON");

        // check type first before calling Azure APIs
        const string eventType = "eventType";
        if (!fileChangeEvent.RootElement.TryGetProperty(eventType, out var eventTypeElement)
            || eventTypeElement.GetString() != "Microsoft.Storage.BlobCreated") {
            return;
        }

        const string topic = "topic";
        if (!fileChangeEvent.RootElement.TryGetProperty(topic, out var topicElement)
            || !_storage.CorpusAccounts().Contains(new ResourceIdentifier(topicElement.GetString()!))) {
            return;
        }

        try {
            // Setting isLastRetryAttempt to false will rethrow any exceptions
            // With the intention that the azure functions runtime will handle requeing
            // the message for us. The difference is for the poison queue, we're handling the
            // requeuing ourselves because azure functions doesn't support retry policies
            // for queue based functions.
            // await FileAdded(_log, fileChangeEvent, false);
            throw new ArgumentException("blah");
        }
        catch {
            await RequeueMessage(msg);
        }
    }

    private async Async.Task FileAdded(ILogTracer log, JsonDocument fileChangeEvent, bool isLastRetryAttempt) {
        var data = fileChangeEvent.RootElement.GetProperty("data");
        var url = data.GetProperty("url").GetString()!;
        var parts = url.Split("/").Skip(3).ToList();

        var container = parts[0];
        var path = string.Join('/', parts.Skip(1));

        log.Info($"file added : {container:Tag:Container} - {path:Tag:Path}");
        await _notificationOperations.NewFiles(Container.Parse(container), path, isLastRetryAttempt);
    }

    private async Async.Task RequeueMessage(string msg) {
        var json = JsonNode.Parse(msg);

        // Messages that are 'manually' requeued by us as opposed to being requeued by the azure functions runtime
        // are treated as new every single time. Thus, the dequeue count doesn't persist.
        // That's why we need to keep track of it ourselves.
        var newCustomDequeueCount = 0;
        if (json!["data"]?["customDequeueCount"] != null) {
            newCustomDequeueCount = json["data"]!["customDequeueCount"]!.GetValue<int>() + 1;
        }

        json!["data"]!["customDequeueCount"] = newCustomDequeueCount;

        if (newCustomDequeueCount > 6) {
            _log.Warning($"Message retried for over a week with no success: {msg}");
        }

        await _context.Queue.QueueObject(
            queueFileChangesPoisonQueueName,
            json,
            StorageType.Config,
            CalculateExponentialBackoff(newCustomDequeueCount))
        .IgnoreResult();
    }

    // Possible return values:
    // 1 - 5 mins
    // 2 - 25 mins
    // 3 - 2 hours 5 mins
    // 4 - 10 hours 25 mins
    // 5 - 2 days 4 hours 5 mins
    // >6 - 7 days +/- 1 day of random variance so all the messages don't dequeue at the same time
    private static TimeSpan CalculateExponentialBackoff(int dequeueCount) {
        var numMinutes = Math.Pow(5, dequeueCount);
        var backoff = TimeSpan.FromMinutes(numMinutes);
        var maxBackoff = MaxTtl();
        return backoff < maxBackoff ? backoff : maxBackoff;
    }

    private static TimeSpan MaxTtl() {
        var variance = new Random().Next(0, 48);
        return TimeSpan.FromHours((24 * 6) + variance);
    }
}
