using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Data.Tables;
using Azure.ResourceManager;
using Azure.ResourceManager.Storage;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Storage.Sas;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;

public enum StorageType {
    Corpus,
    Config
}

public interface IStorage {
    public IReadOnlyList<ResourceIdentifier> CorpusAccounts();
    public ResourceIdentifier GetPrimaryAccount(StorageType storageType);
    public IReadOnlyList<ResourceIdentifier> GetAccounts(StorageType storageType);

    /// Picks either the single primary account or a random secondary account.
    public ResourceIdentifier ChooseAccount(StorageType storageType) {
        var accounts = GetAccounts(storageType);
        if (!accounts.Any()) {
            throw new InvalidOperationException($"no storage accounts for {storageType}");
        }

        if (accounts.Count == 1) {
            return accounts[0];
        }

        // Use a random secondary storage account if any are available.  This
        // reduces IOP contention for the Storage Queues, which are only available
        // on primary accounts
        //
        // security note: this is not used as a security feature
        var secondaryAccounts = accounts.Skip(1).ToList();
        return secondaryAccounts[Random.Shared.Next(secondaryAccounts.Count)];
    }

    public Task<BlobServiceClient> GetBlobServiceClientForAccount(ResourceIdentifier accountId)
        => GetBlobServiceClientForAccountName(accountId.Name);

    public Task<BlobServiceClient> GetBlobServiceClientForAccountName(string accountName);

    public Uri GenerateBlobContainerSasUri(
        BlobContainerSasPermissions permissions,
        BlobContainerClient containerClient,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow)
        => containerClient.GenerateSasUri(new BlobSasBuilder(permissions, timeWindow.endTime) {
            StartsOn = timeWindow.startTime,
            BlobContainerName = containerClient.Name,
        });

    public Uri GenerateBlobSasUri(
        BlobSasPermissions permissions,
        BlobClient blobClient,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow)
        => blobClient.GenerateSasUri(new BlobSasBuilder(permissions, timeWindow.endTime) {
            StartsOn = timeWindow.startTime,
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name
        });

    public Task<TableServiceClient> GetTableServiceClientForAccount(ResourceIdentifier accountId)
        => GetTableServiceClientForAccountName(accountId.Name);

    public Task<TableServiceClient> GetTableServiceClientForAccountName(string accountName);

    public Task<QueueServiceClient> GetQueueServiceClientForAccount(ResourceIdentifier accountId)
        => GetQueueServiceClientForAccountName(accountId.Name);

    public Task<QueueServiceClient> GetQueueServiceClientForAccountName(string accountName);

    public Uri GenerateQueueSasUri(
        QueueSasPermissions permissions,
        QueueClient queueClient,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow)
        => queueClient.GenerateSasUri(new QueueSasBuilder(permissions, timeWindow.endTime) {
            StartsOn = timeWindow.startTime,
        });
}

public sealed class Storage : IStorage {
    private readonly ICreds _creds;
    private readonly ArmClient _armClient;
    private readonly ILogger _log;
    private readonly IServiceConfig _config;
    private readonly IMemoryCache _cache;

    public Storage(ICreds creds,
        ILogger<Storage> log,
        IServiceConfig config,
        IMemoryCache cache) {
        _creds = creds;
        _armClient = creds.ArmClient;
        _log = log;
        _config = config;
        _cache = cache;
    }

    public ResourceIdentifier GetFuncStorage() => _config.OneFuzzFuncStorage;

    public ResourceIdentifier GetFuzzStorage() => _config.OneFuzzDataStorage;

    public ArmClient GetMgmtClient() => _armClient;

    private static readonly object _corpusAccountsKey = new(); // we only need equality/hashcode
    public IReadOnlyList<ResourceIdentifier> CorpusAccounts() {
        return _cache.GetOrCreate<IReadOnlyList<ResourceIdentifier>>(_corpusAccountsKey, cacheEntry => {
            var skip = GetFuncStorage();
            var results = new List<ResourceIdentifier> { GetFuzzStorage() };

            var client = GetMgmtClient();
            var group = _creds.GetResourceGroupResourceIdentifier();

            const string storageTypeTagKey = "storage_type";

            var resourceGroup = client.GetResourceGroupResource(group);
            foreach (var account in resourceGroup.GetStorageAccounts()) {
                if (account.Id == skip) {
                    continue;
                }

                if (results.Contains(account.Id)) {
                    continue;
                }

                if (!account.Data.Tags.ContainsKey(storageTypeTagKey)
                    || account.Data.Tags[storageTypeTagKey] != "corpus") {
                    continue;
                }

                results.Add(account.Id);
            }

            _log.LogInformation("corpus accounts: {results}", JsonSerializer.Serialize(results));
            return results;
        })!; // NULLABLE: only this method inserts _corpusAccountsKey so it cannot be null
    }

    public ResourceIdentifier GetPrimaryAccount(StorageType storageType)
        => storageType switch {
            StorageType.Corpus => GetFuzzStorage(),
            StorageType.Config => GetFuncStorage(),
            var x => throw new NotSupportedException($"invalid StorageType: {x}"),
        };

    sealed record GetStorageAccountKey_CacheKey(ResourceIdentifier Identifier);
    public Async.Task<string?> GetStorageAccountKey(string accountName) {
        var resourceGroupId = _creds.GetResourceGroupResourceIdentifier();
        var storageAccountId = StorageAccountResource.CreateResourceIdentifier(resourceGroupId.SubscriptionId, resourceGroupId.Name, accountName);
        return _cache.GetOrCreateAsync(new GetStorageAccountKey_CacheKey(storageAccountId), async cacheEntry => {
            var armClient = GetMgmtClient();
            var keys = await armClient.GetStorageAccountResource(storageAccountId).GetKeysAsync();
            return keys.Value.Keys.FirstOrDefault()?.Value;
        });
    }

    public IReadOnlyList<ResourceIdentifier> GetAccounts(StorageType storageType)
        => storageType switch {
            StorageType.Corpus => CorpusAccounts(),
            StorageType.Config => new[] { GetFuncStorage() },
            _ => throw new NotSupportedException(),
        };

    private static Uri GetTableEndpoint(string accountName)
        => new($"https://{accountName}.table.core.windows.net/");

    private static Uri GetQueueEndpoint(string accountName)
        => new($"https://{accountName}.queue.core.windows.net/");

    private static Uri GetBlobEndpoint(string accountName)
        => new($"https://{accountName}.blob.core.windows.net/");

    // According to guidance these should be reused as they manage HttpClients,
    // so we cache them all by account:

    sealed record BlobClientKey(string AccountName);
    public Task<BlobServiceClient> GetBlobServiceClientForAccountName(string accountName) {
        return _cache.GetOrCreate(new BlobClientKey(accountName), async cacheEntry => {
            cacheEntry.Priority = CacheItemPriority.NeverRemove;
            var accountKey = await GetStorageAccountKey(accountName);
            var skc = new StorageSharedKeyCredential(accountName, accountKey);
            return new BlobServiceClient(GetBlobEndpoint(accountName), skc);
        })!; // NULLABLE: only this method inserts BlobClientKey so result cannot be null
    }

    sealed record TableClientKey(string AccountName);
    public Task<TableServiceClient> GetTableServiceClientForAccountName(string accountName)
        => _cache.GetOrCreate(new TableClientKey(accountName), async cacheEntry => {
            cacheEntry.Priority = CacheItemPriority.NeverRemove;
            var accountKey = await GetStorageAccountKey(accountName);
            var skc = new TableSharedKeyCredential(accountName, accountKey);
            return new TableServiceClient(GetTableEndpoint(accountName), skc);
        })!; // NULLABLE: only this method inserts TableClientKey so result cannot be null

    sealed record QueueClientKey(string AccountName);
    private static readonly QueueClientOptions _queueClientOptions = new() { MessageEncoding = QueueMessageEncoding.Base64 };
    public Task<QueueServiceClient> GetQueueServiceClientForAccountName(string accountName)
        => _cache.GetOrCreateAsync(new QueueClientKey(accountName), async cacheEntry => {
            cacheEntry.Priority = CacheItemPriority.NeverRemove;
            var accountKey = await GetStorageAccountKey(accountName);
            var skc = new StorageSharedKeyCredential(accountName, accountKey);
            return new QueueServiceClient(GetQueueEndpoint(accountName), skc, _queueClientOptions);
        })!; // NULLABLE: only this method inserts QueueClientKey so result cannot be null
}
