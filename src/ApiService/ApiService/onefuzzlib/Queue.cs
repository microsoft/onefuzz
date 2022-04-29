using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage;
using Azure.Storage.Queues;
using Azure.Storage.Sas;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;
public interface IQueue {
    Async.Task SendMessage(string name, byte[] message, StorageType storageType, TimeSpan? visibilityTimeout = null, TimeSpan? timeToLive = null);
    Async.Task<bool> QueueObject<T>(string name, T obj, StorageType storageType, TimeSpan? visibilityTimeout = null);
    Async.Task<Uri?> GetQueueSas(string name, StorageType storageType, QueueSasPermissions permissions, TimeSpan? duration = null);
}


public class Queue : IQueue {
    IStorage _storage;
    ILogTracer _log;

    static TimeSpan DEFAULT_DURATION = TimeSpan.FromDays(30);

    public Queue(IStorage storage, ILogTracer log) {
        _storage = storage;
        _log = log;
    }


    public async Async.Task SendMessage(string name, byte[] message, StorageType storageType, TimeSpan? visibilityTimeout = null, TimeSpan? timeToLive = null) {
        var queue = await GetQueue(name, storageType);
        if (queue != null) {
            await queue.SendMessageAsync(Convert.ToBase64String(message), visibilityTimeout: visibilityTimeout, timeToLive: timeToLive);
        }
    }

    public async Task<QueueClient?> GetQueue(string name, StorageType storageType) {
        var client = await GetQueueClient(storageType);
        return client.GetQueueClient(name);
    }


    public async Task<QueueServiceClient> GetQueueClient(StorageType storageType) {
        var accountId = _storage.GetPrimaryAccount(storageType);
        //_logger.LogDEbug("getting blob container (account_id: %s)", account_id)
        var (name, key) = await _storage.GetStorageAccountNameAndKey(accountId);
        var accountUrl = new Uri($"https://{name}.queue.core.windows.net");
        var client = new QueueServiceClient(accountUrl, new StorageSharedKeyCredential(name, key));
        return client;
    }

    public async Task<bool> QueueObject<T>(string name, T obj, StorageType storageType, TimeSpan? visibilityTimeout) {
        var queue = await GetQueue(name, storageType) ?? throw new Exception($"unable to queue object, no such queue: {name}");

        var serialized = JsonSerializer.Serialize(obj, EntityConverter.GetJsonSerializerOptions());
        //var encoded = Encoding.UTF8.GetBytes(serialized);
        var response = await queue.SendMessageAsync(serialized, visibilityTimeout: visibilityTimeout);
        return !response.GetRawResponse().IsError;
    }

    public async Task<Uri?> GetQueueSas(string name, StorageType storageType, QueueSasPermissions permissions, TimeSpan? duration) {
        var queue = await GetQueue(name, storageType) ?? throw new Exception($"unable to queue object, no such queue: {name}");
        var sasaBuilder = new QueueSasBuilder(permissions, DateTimeOffset.UtcNow + (duration ?? DEFAULT_DURATION));
        var url = queue.GenerateSasUri(sasaBuilder);
        return url;
    }
}
