using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
namespace Microsoft.OneFuzz.Service.Functions;

public class QueueFileChanges {
    // The number of time the function will be retried if an error occurs
    // https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-queue-trigger?tabs=csharp#poison-messages
    const int MAX_DEQUEUE_COUNT = 5;

    private const string QueueFileChangesPoisonQueueName = "file-changes-poison";
    private const string QueueFileChangesQueueName = "file-changes";

    private readonly ILogger _log;

    private readonly IStorage _storage;

    private readonly INotificationOperations _notificationOperations;

    private readonly IOnefuzzContext _context;

    public QueueFileChanges(ILogger<QueueFileChanges> log, IStorage storage, INotificationOperations notificationOperations, IOnefuzzContext context) {
        _log = log;
        _storage = storage;
        _notificationOperations = notificationOperations;
        _context = context;
    }

    [Function("QueueFileChanges")]
    public async Async.Task Run(
        [QueueTrigger(QueueFileChangesQueueName, Connection = "AzureWebJobsStorage")] string msg) {
        var fileChangeEvent = JsonSerializer.Deserialize<JsonDocument>(msg, EntityConverter.GetJsonSerializerOptions());

        _ = fileChangeEvent ?? throw new ArgumentException("Unable to parse queue trigger as JSON");

        // check type first before calling Azure APIs
        const string eventType = "eventType";
        const string expectedEventType = "Microsoft.Storage.BlobCreated";
        if (!fileChangeEvent.RootElement.TryGetProperty(eventType, out var eventTypeElement)
            || eventTypeElement.GetString() != expectedEventType) {
            _log.AddTag("queueMessage", msg);
            _log.LogInformation("Expected fileChangeEvent to contain a property named '{eventType}' with value equal to {expectedEventType}", eventType, expectedEventType);
            return;
        }

        const string topic = "topic";
        if (!fileChangeEvent.RootElement.TryGetProperty(topic, out var topicElement)
            || !_storage.CorpusAccounts().Contains(new ResourceIdentifier(topicElement.GetString()!))) {
            _log.AddTag("queueMessage", msg);
            _log.LogInformation("Expected fileChangeEvent to contain a property named '{topic}' with a value contained in corpus accounts", topic);
            return;
        }

        var storageAccount = new ResourceIdentifier(topicElement.GetString()!);

        try {
            // Setting isLastRetryAttempt to false will rethrow any exceptions
            // With the intention that the azure functions runtime will handle requeing
            // the message for us. The difference is for the poison queue, we're handling the
            // requeuing ourselves because azure functions doesn't support retry policies
            // for queue based functions.

            var result = await FileAdded(storageAccount, fileChangeEvent, isLastRetryAttempt: false);
            if (!result.IsOk && result.ErrorV.Code == ErrorCode.ADO_WORKITEM_PROCESSING_DISABLED) {
                await RequeueMessage(msg, TimeSpan.FromDays(1));
            }
        } catch (Exception e) {
            _log.LogError(e, "File Added failed");
            await RequeueMessage(msg);
        }
    }

    private async Async.Task<OneFuzzResultVoid> FileAdded(ResourceIdentifier storageAccount, JsonDocument fileChangeEvent, bool isLastRetryAttempt) {
        var data = fileChangeEvent.RootElement.GetProperty("data");
        var url = data.GetProperty("url").GetString()!;
        var parts = url.Split("/").Skip(3).ToList();

        var container = Container.Parse(parts[0]);
        var path = string.Join('/', parts.Skip(1));

        _log.LogInformation("file added : {Container} - {Path}", container.String, path);

        var (_, result) = await (
            ApplyRetentionPolicy(storageAccount, container, path),
            _notificationOperations.NewFiles(container, path, isLastRetryAttempt));

        return result;
    }

    private async Async.Task<bool> ApplyRetentionPolicy(ResourceIdentifier storageAccount, Container container, string path) {
        if (await _context.FeatureManagerSnapshot.IsEnabledAsync(FeatureFlagConstants.EnableContainerRetentionPolicies)) {
            // default retention period can be applied to the container
            // if one exists, we will set the expiry date on the newly-created blob, if it doesn't already have one
            var account = await _storage.GetBlobServiceClientForAccount(storageAccount);
            var containerClient = account.GetBlobContainerClient(container.String);
            var containerProps = await containerClient.GetPropertiesAsync();
            var retentionPeriod = RetentionPolicyUtils.GetContainerRetentionPeriodFromMetadata(containerProps.Value.Metadata);
            if (!retentionPeriod.IsOk) {
                _log.LogError("invalid retention period: {Error}", retentionPeriod.ErrorV);
            } else if (retentionPeriod.OkV is TimeSpan period) {
                var blobClient = containerClient.GetBlobClient(path);
                var tags = (await blobClient.GetTagsAsync()).Value.Tags;
                var expiryDate = DateTime.UtcNow + period;
                var tag = RetentionPolicyUtils.CreateExpiryDateTag(DateOnly.FromDateTime(expiryDate));
                if (tags.TryAdd(tag.Key, tag.Value)) {
                    _ = await blobClient.SetTagsAsync(tags);
                    _log.LogInformation("applied container retention policy ({Policy}) to {Path}", period, path);
                    return true;
                }
            }
        }

        return false;
    }

    private async Async.Task RequeueMessage(string msg, TimeSpan? visibilityTimeout = null) {
        var json = JsonNode.Parse(msg);

        // Messages that are 'manually' requeued by us as opposed to being requeued by the azure functions runtime
        // are treated as new every single time. Thus, the dequeue count doesn't persist.
        // That's why we need to keep track of it ourselves.
        var newCustomDequeueCount = 0;
        if (json!["data"]?["customDequeueCount"] != null) {
            newCustomDequeueCount = json["data"]!["customDequeueCount"]!.GetValue<int>();
        }

        if (newCustomDequeueCount > MAX_DEQUEUE_COUNT) {
            _log.LogWarning("Message retried more than {MAX_DEQUEUE_COUNT} times with no success: {msg}", MAX_DEQUEUE_COUNT, msg);
            await _context.Queue.QueueObject(
                QueueFileChangesPoisonQueueName,
                json,
                StorageType.Config)
                .IgnoreResult();
        } else {
            json!["data"]!["customDequeueCount"] = newCustomDequeueCount + 1;
            await _context.Queue.QueueObject(
                QueueFileChangesQueueName,
                json,
                StorageType.Config,
                visibilityTimeout ?? CalculateExponentialBackoff(newCustomDequeueCount))
                .IgnoreResult();
        }
    }

    // Possible return values:
    // 1 - 5 mins
    // 2 - 25 mins
    // 3 - 2 hours 5 mins
    // 4 - 10 hours 25 mins
    // >5 - 2 days +/ 6 hours variance so all the messages don't dequeue at the same time
    public static TimeSpan CalculateExponentialBackoff(int dequeueCount) {
        var numMinutes = Math.Pow(5, dequeueCount);
        var backoff = TimeSpan.FromMinutes(numMinutes);
        var maxBackoff = MaxTtl();
        return backoff < maxBackoff ? backoff : maxBackoff;
    }

    private static TimeSpan MaxTtl() {
        var variance = Random.Shared.Next(0, 6);
        return TimeSpan.FromHours((24 * 2) + variance);
    }

}
