using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Azure.Storage.Sas;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;
public interface IQueue {
    Async.Task SendMessage(string name, string message, StorageType storageType, TimeSpan? visibilityTimeout = null, TimeSpan? timeToLive = null);
    Async.Task<bool> QueueObject<T>(string name, T obj, StorageType storageType, TimeSpan? visibilityTimeout = null, TimeSpan? timeToLive = null);
    Task<Uri> GetQueueSas(string name, StorageType storageType, QueueSasPermissions permissions, TimeSpan? duration = null);
    ResourceIdentifier GetResourceId(string queueName, StorageType storageType);
    Task<IList<T>> PeekQueue<T>(string name, StorageType storageType);
    Async.Task<bool> RemoveFirstMessage(string name, StorageType storageType);
    Async.Task ClearQueue(string name, StorageType storageType);
    Async.Task DeleteQueue(string name, StorageType storageType);
    Async.Task CreateQueue(string name, StorageType storageType);
    IAsyncEnumerable<QueueItem> ListQueues(StorageType storageType);
}



public class Queue : IQueue {
    readonly IStorage _storage;
    readonly ILogTracer _log;

    static readonly TimeSpan DEFAULT_DURATION = TimeSpan.FromDays(30);

    public Queue(IStorage storage, ILogTracer log) {
        _storage = storage;
        _log = log;
    }

    public async IAsyncEnumerable<QueueItem> ListQueues(StorageType storageType) {
        var queueServiceClient = await GetQueueClientService(storageType);
        await foreach (var q in queueServiceClient.GetQueuesAsync()) {
            yield return q;
        }
    }

    public async Async.Task SendMessage(string name, string message, StorageType storageType, TimeSpan? visibilityTimeout = null, TimeSpan? timeToLive = null) {
        var queue = await GetQueueClient(name, storageType);
        try {
            await queue.SendMessageAsync(message, visibilityTimeout: visibilityTimeout, timeToLive: timeToLive);
        } catch (Exception ex) {
            _log.Exception(ex, $"Failed to send message {message}");
            throw;
        }
    }

    public async Task<QueueClient> GetQueueClient(string name, StorageType storageType)
        => (await GetQueueClientService(storageType)).GetQueueClient(name);

    public Task<QueueServiceClient> GetQueueClientService(StorageType storageType)
        => _storage.GetQueueServiceClientForAccount(_storage.GetPrimaryAccount(storageType));

    public async Task<bool> QueueObject<T>(string name, T obj, StorageType storageType, TimeSpan? visibilityTimeout = null, TimeSpan? timeToLive = null) {
        var queueClient = await GetQueueClient(name, storageType);
        try {
            var serialized = JsonSerializer.Serialize(obj, EntityConverter.GetJsonSerializerOptions());
            var res = await queueClient.SendMessageAsync(serialized, visibilityTimeout: visibilityTimeout, timeToLive);
            if (res.GetRawResponse().IsError) {
                _log.Error($"Failed to send message {serialized} in queue {name} due to {res.GetRawResponse().ReasonPhrase}");
                return false;
            } else {
                return true;
            }
        } catch (Exception ex) {
            _log.Exception(ex, $"Failed to queue message in queue {name}");
            return false;
        }
    }

    public async Task<Uri> GetQueueSas(string name, StorageType storageType, QueueSasPermissions permissions, TimeSpan? duration) {
        var queueClient = await GetQueueClient(name, storageType);
        var now = DateTimeOffset.UtcNow;
        return _storage.GenerateQueueSasUri(
            permissions,
            queueClient,
            (now, now + (duration ?? DEFAULT_DURATION)));
    }

    public async Async.Task CreateQueue(string name, StorageType storageType) {
        var client = await GetQueueClient(name, storageType);
        var resp = await client.CreateIfNotExistsAsync();

        if (resp is not null && resp.IsError) {
            _log.Error($"failed to create queue {name} due to {resp.ReasonPhrase}");
        }
    }

    public async Async.Task DeleteQueue(string name, StorageType storageType) {
        var client = await GetQueueClient(name, storageType);
        var resp = await client.DeleteIfExistsAsync();
        if (resp.GetRawResponse() is not null && resp.GetRawResponse().IsError) {
            _log.Error($"failed to delete queue {name} due to {resp.GetRawResponse().ReasonPhrase}");
        }
    }

    public async Async.Task ClearQueue(string name, StorageType storageType) {
        var client = await GetQueueClient(name, storageType);
        var resp = await client.ClearMessagesAsync();
        if (resp is not null && resp.IsError) {
            _log.Error($"failed to clear the queue {name} due to {resp.ReasonPhrase}");
        }
    }

    public async Async.Task<bool> RemoveFirstMessage(string name, StorageType storageType) {
        var client = await GetQueueClient(name, storageType);
        try {
            var msgs = await client.ReceiveMessagesAsync();
            foreach (var msg in msgs.Value) {
                var resp = await client.DeleteMessageAsync(msg.MessageId, msg.PopReceipt);
                if (resp.IsError) {
                    _log.Error($"failed to delete message from the queue {name} due to {resp.ReasonPhrase}");
                    return false;
                } else {
                    return true;
                }
            }
        } catch (RequestFailedException ex) when (ex.Status == 404 || ex.ErrorCode == "QueueNotFound") {
            _log.Info($"tried to remove message from queue {name} but it doesn't exist");
            return false;
        }

        return false;
    }

    public async Task<IList<T>> PeekQueue<T>(string name, StorageType storageType) {
        var client = await GetQueueClient(name, storageType);

        var result = new List<T>();

        var msgs = await client.PeekMessagesAsync(client.MaxPeekableMessages);
        if (msgs is null) {
            return result;
        } else if (msgs.GetRawResponse().IsError) {

            _log.Error($"failed to peek messages due to {msgs.GetRawResponse().ReasonPhrase}");
            return result;
        } else {
            foreach (var msg in msgs.Value) {

                var obj = JsonSerializer.Deserialize<T>(msg.Body.ToString(), EntityConverter.GetJsonSerializerOptions());
                if (obj is not null) {
                    result.Add(obj);
                }
            }
        }
        return result;
    }

    public ResourceIdentifier GetResourceId(string queueName, StorageType storageType) {
        var account = _storage.GetPrimaryAccount(storageType);
        return new ResourceIdentifier($"{account}/services/queue/queues/{queueName}");
    }

}
