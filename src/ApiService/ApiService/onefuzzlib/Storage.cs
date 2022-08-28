using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Storage;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Queues;
using Azure.Storage.Sas;
using Microsoft.Extensions.Caching.Memory;

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

    public BlobServiceClient GetBlobServiceClientForAccount(ResourceIdentifier accountId);

    public BlobServiceClient GetBlobServiceClientForAccountName(string accountName);

    public Task<Uri> GenerateBlobContainerSasUri(
        BlobContainerSasPermissions permissions,
        BlobContainerClient containerClient,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow);

    public Task<Uri> GenerateBlobSasUri(
        BlobSasPermissions permissions,
        BlobClient blobClient,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow);

    public TableServiceClient GetTableServiceClientForAccount(ResourceIdentifier accountId);

    public QueueServiceClient GetQueueServiceClientForAccount(ResourceIdentifier accountId);
    public Task<Uri> GenerateQueueSasUri(
        QueueSasPermissions permissions,
        string accountName,
        string queueName,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow);
}

public sealed class Storage : IStorage {
    private readonly ICreds _creds;
    private readonly ArmClient _armClient;
    private readonly ILogTracer _log;
    private readonly IServiceConfig _config;
    private readonly IMemoryCache _cache;

    public Storage(ICreds creds,
        ILogTracer log,
        IServiceConfig config,
        IMemoryCache cache) {
        _creds = creds;
        _armClient = creds.ArmClient;
        _log = log;
        _config = config;
        _cache = cache;
    }

    public ResourceIdentifier GetFuncStorage() {
        return _config.OneFuzzFuncStorage
            ?? throw new Exception("Func storage env var is missing");
    }

    public ResourceIdentifier GetFuzzStorage() {
        return _config.OneFuzzDataStorage
            ?? throw new Exception("Fuzz storage env var is missing");
    }

    public ArmClient GetMgmtClient() {
        return _armClient;
    }

    public IReadOnlyList<ResourceIdentifier> CorpusAccounts() {
        return _cache.GetOrCreate<IReadOnlyList<ResourceIdentifier>>("CorpusAccounts", cacheEntry => {
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

            _log.Info($"corpus accounts: {JsonSerializer.Serialize(results)}");
            return results;
        });
    }

    public ResourceIdentifier GetPrimaryAccount(StorageType storageType)
        => storageType switch {
            StorageType.Corpus => GetFuzzStorage(),
            StorageType.Config => GetFuncStorage(),
            var x => throw new NotSupportedException($"invalid StorageType: {x}"),
        };

    record GetStorageAccountKey_CacheKey(ResourceIdentifier Identifier);
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

    public BlobServiceClient GetBlobServiceClientForAccount(ResourceIdentifier accountId)
        => GetBlobServiceClientForAccountName(accountId.Name);

    record BlobClientKey(string AccountName);
    public BlobServiceClient GetBlobServiceClientForAccountName(string accountName) {
        return _cache.GetOrCreate(new BlobClientKey(accountName), cacheEntry => {
            cacheEntry.Priority = CacheItemPriority.NeverRemove;
            return new BlobServiceClient(GetBlobEndpoint(accountName), new DefaultAzureCredential());
        });
    }

    record TableClientKey(string AccountName);
    public TableServiceClient GetTableServiceClientForAccount(ResourceIdentifier accountId) {
        var accountName = accountId.Name;
        return _cache.GetOrCreate(new TableClientKey(accountName), cacheEntry => {
            cacheEntry.Priority = CacheItemPriority.NeverRemove;
            return new TableServiceClient(GetTableEndpoint(accountName), new DefaultAzureCredential());
        });
    }

    record QueueClientKey(string AccountName);
    private static readonly QueueClientOptions _queueClientOptions = new() { MessageEncoding = QueueMessageEncoding.Base64 };
    public QueueServiceClient GetQueueServiceClientForAccount(ResourceIdentifier accountId) {
        var accountName = accountId.Name;
        return _cache.GetOrCreate(new QueueClientKey(accountName), cacheEntry => {
            cacheEntry.Priority = CacheItemPriority.NeverRemove;
            return new QueueServiceClient(GetQueueEndpoint(accountName), new DefaultAzureCredential(), _queueClientOptions);
        });
    }

    record SharedKeyQueueClient_CacheKey(string AccountName);
    private Task<QueueServiceClient> GetSharedKeyQueueServiceClientForAccountName(string accountName) {
        return _cache.GetOrCreateAsync(new SharedKeyQueueClient_CacheKey(accountName), async cacheEntry => {
            var accountKey = await GetStorageAccountKey(accountName);
            var skc = new StorageSharedKeyCredential(accountName, accountKey);
            return new QueueServiceClient(GetQueueEndpoint(accountName), skc);
        });
    }

    public async Task<Uri> GenerateBlobContainerSasUri(
        BlobContainerSasPermissions permissions,
        BlobContainerClient containerClient,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow) {

        var serviceClient = containerClient.GetParentBlobServiceClient();
        var delegationKey = await serviceClient.GetUserDelegationKeyAsync(timeWindow.startTime, timeWindow.endTime);

        var sasBuilder = new BlobSasBuilder(permissions, timeWindow.endTime) {
            StartsOn = timeWindow.startTime,
            BlobContainerName = containerClient.Name,
        };

        var blobUriBuilder = new BlobUriBuilder(containerClient.Uri) {
            Sas = sasBuilder.ToSasQueryParameters(delegationKey, serviceClient.AccountName),
        };

        return blobUriBuilder.ToUri();
    }

    public async Task<Uri> GenerateBlobSasUri(
        BlobSasPermissions permissions,
        BlobClient blobClient,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow) {

        var containerClient = blobClient.GetParentBlobContainerClient();
        var serviceClient = containerClient.GetParentBlobServiceClient();
        var delegationKey = await serviceClient.GetUserDelegationKeyAsync(timeWindow.startTime, timeWindow.endTime);

        var sasBuilder = new BlobSasBuilder(permissions, timeWindow.endTime) {
            StartsOn = timeWindow.startTime,
            BlobContainerName = containerClient.Name,
            BlobName = blobClient.Name
        };

        var blobUriBuilder = new BlobUriBuilder(blobClient.Uri) {
            Sas = sasBuilder.ToSasQueryParameters(delegationKey, serviceClient.AccountName),
        };

        return blobUriBuilder.ToUri();
    }

    public async Task<Uri> GenerateQueueSasUri(
        QueueSasPermissions permissions,
        string accountName,
        string queueName,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow) {

        // must have the shared-key client to build a SAS URI for queues:
        var serviceClient = await GetSharedKeyQueueServiceClientForAccountName(accountName);
        var sasBuilder = new QueueSasBuilder(permissions, timeWindow.endTime) {
            StartsOn = timeWindow.startTime,
        };

        return serviceClient.GetQueueClient(queueName).GenerateSasUri(sasBuilder);
    }
}
