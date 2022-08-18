using System.Text.Json;
using Azure.Core;
using Azure.Data.Tables;
using Azure.ResourceManager;
using Azure.ResourceManager.Storage;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.OneFuzz.Service;

public enum StorageType {
    Corpus,
    Config
}

public interface IStorage {
    public IReadOnlyList<string> CorpusAccounts();
    public string GetPrimaryAccount(StorageType storageType);
    public IReadOnlyList<string> GetAccounts(StorageType storageType);

    public Uri GetTableEndpoint(string accountId);

    public Uri GetQueueEndpoint(string accountId);

    public Uri GetBlobEndpoint(string accountId);

    public Async.Task<(string, string)> GetStorageAccountNameAndKey(string accountId);

    public Async.Task<string?> GetStorageAccountNameKeyByName(string accountName);

    /// Picks either the single primary account or a random secondary account.
    public string ChooseAccount(StorageType storageType) {
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

    public async Async.Task<BlobServiceClient> GetBlobServiceClientForAccount(string accountId) {
        var (accountName, accountKey) = await GetStorageAccountNameAndKey(accountId);
        var storageKeyCredential = new StorageSharedKeyCredential(accountName, accountKey);
        var accountUrl = GetBlobEndpoint(accountName);
        return new BlobServiceClient(accountUrl, storageKeyCredential);
    }

    public async Async.Task<TableServiceClient> GetTableServiceClientForAccount(string accountId) {
        var (accountName, accountKey) = await GetStorageAccountNameAndKey(accountId);
        var storageKeyCredential = new TableSharedKeyCredential(accountName, accountKey);
        var accountUrl = GetTableEndpoint(accountName);
        return new TableServiceClient(accountUrl, storageKeyCredential);
    }
}

public sealed class Storage : IStorage, IDisposable {
    private readonly ICreds _creds;
    private readonly ArmClient _armClient;
    private readonly ILogTracer _log;
    private readonly IServiceConfig _config;
    private readonly MemoryCache _cache;

    public Storage(ICreds creds, ILogTracer log, IServiceConfig config) {
        _creds = creds;
        _armClient = creds.ArmClient;
        _log = log;
        _config = config;
        _cache = new MemoryCache(new MemoryCacheOptions() {

        });
    }

    public string GetFuncStorage() {
        return _config.OneFuzzFuncStorage
            ?? throw new Exception("Func storage env var is missing");
    }

    public string GetFuzzStorage() {
        return _config.OneFuzzDataStorage
            ?? throw new Exception("Fuzz storage env var is missing");
    }

    public ArmClient GetMgmtClient() {
        return _armClient;
    }

    public IReadOnlyList<string> CorpusAccounts() {
        return _cache.GetOrCreate<IReadOnlyList<string>>("CorpusAccounts", cacheEntry => {
            var skip = GetFuncStorage();
            var results = new List<string> { GetFuzzStorage() };

            var client = GetMgmtClient();
            var group = _creds.GetResourceGroupResourceIdentifier();

            const string storageTypeTagKey = "storage_type";

            var resourceGroup = client.GetResourceGroupResource(group);
            foreach (var account in resourceGroup.GetStorageAccounts()) {
                if (account.Id == skip) {
                    continue;
                }

                if (results.Contains(account.Id!)) {
                    continue;
                }

                if (!account.Data.Tags.ContainsKey(storageTypeTagKey)
                    || account.Data.Tags[storageTypeTagKey] != "corpus") {
                    continue;
                }

                results.Add(account.Id!);
            }

            _log.Info($"corpus accounts: {JsonSerializer.Serialize(results)}");
            return results;
        });
    }

    public string GetPrimaryAccount(StorageType storageType)
        => storageType switch {
            StorageType.Corpus => GetFuzzStorage(),
            StorageType.Config => GetFuncStorage(),
            var x => throw new NotSupportedException($"invalid StorageType: {x}"),
        };

    public Async.Task<(string, string)> GetStorageAccountNameAndKey(string accountId) {
        return _cache.GetOrCreateAsync<(string, string)>($"GetStorageAccountNameAndKey-{accountId}", async cacheEntry => {
            var resourceId = new ResourceIdentifier(accountId);
            var armClient = GetMgmtClient();
            var storageAccount = armClient.GetStorageAccountResource(resourceId);
            var keys = await storageAccount.GetKeysAsync();
            var key = keys.Value.Keys.FirstOrDefault() ?? throw new Exception("no keys found");
            return (resourceId.Name, key.Value);
        });
    }

    public Async.Task<string?> GetStorageAccountNameKeyByName(string accountName) {
        return _cache.GetOrCreateAsync<string?>($"GetStorageAccountNameKeyByName-{accountName}", async cacheEntry => {
            var armClient = GetMgmtClient();
            var resourceGroup = _creds.GetResourceGroupResourceIdentifier();
            var storageAccount = await armClient.GetResourceGroupResource(resourceGroup).GetStorageAccountAsync(accountName);
            var keys = await storageAccount.Value.GetKeysAsync();
            var key = keys.Value.Keys.FirstOrDefault();
            return key?.Value;
        });
    }


    public IReadOnlyList<string> GetAccounts(StorageType storageType) {
        switch (storageType) {
            case StorageType.Corpus:
                return CorpusAccounts();
            case StorageType.Config:
                return new[] { GetFuncStorage() };
            default:
                throw new NotSupportedException();
        }
    }

    public Uri GetTableEndpoint(string accountId)
        => new($"https://{accountId}.table.core.windows.net/");

    public Uri GetQueueEndpoint(string accountId)
        => new($"https://{accountId}.queue.core.windows.net/");

    public Uri GetBlobEndpoint(string accountId)
        => new($"https://{accountId}.blob.core.windows.net/");

    public void Dispose() {
        _cache.Dispose();
    }
}
