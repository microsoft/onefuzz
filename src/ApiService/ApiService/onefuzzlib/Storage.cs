using System.Text.Json;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Storage;

namespace Microsoft.OneFuzz.Service;

public enum StorageType {
    Corpus,
    Config
}

public interface IStorage {
    public IEnumerable<string> CorpusAccounts();
    string GetPrimaryAccount(StorageType storageType);

    public Uri GetTableEndpoint(string accountId);

    public Uri GetQueueEndpoint(string accountId);

    public Uri GetBlobEndpoint(string accountId);

    public Async.Task<(string, string)> GetStorageAccountNameAndKey(string accountId);

    public Async.Task<string?> GetStorageAccountNameAndKeyByName(string accountName);

    public IEnumerable<string> GetAccounts(StorageType storageType);
}

public class Storage : IStorage {
    private ICreds _creds;
    private ArmClient _armClient;
    private ILogTracer _log;
    private IServiceConfig _config;

    public Storage(ICreds creds, ILogTracer log, IServiceConfig config) {
        _creds = creds;
        _armClient = creds.ArmClient;
        _log = log;
        _config = config;
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

    // TODO: @cached
    public IEnumerable<string> CorpusAccounts() {
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

            if (string.IsNullOrEmpty(account.Data.PrimaryEndpoints.Blob)) {
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
    }

    public string GetPrimaryAccount(StorageType storageType) {
        return
            storageType switch {
                StorageType.Corpus => GetFuzzStorage(),
                StorageType.Config => GetFuncStorage(),
                _ => throw new NotImplementedException(),
            };
    }

    public async Async.Task<(string, string)> GetStorageAccountNameAndKey(string accountId) {
        var resourceId = new ResourceIdentifier(accountId);
        var armClient = GetMgmtClient();
        var storageAccount = armClient.GetStorageAccountResource(resourceId);
        var keys = await storageAccount.GetKeysAsync();
        var key = keys.Value.Keys.FirstOrDefault() ?? throw new Exception("no keys found");
        return (resourceId.Name, key.Value);
    }

    public async Async.Task<string?> GetStorageAccountNameAndKeyByName(string accountName) {
        var armClient = GetMgmtClient();
        var resourceGroup = _creds.GetResourceGroupResourceIdentifier();
        var storageAccount = await armClient.GetResourceGroupResource(resourceGroup).GetStorageAccountAsync(accountName);
        var keys = await storageAccount.Value.GetKeysAsync();
        var key = keys.Value.Keys.FirstOrDefault();
        return key?.Value;
    }

    public string ChooseAccounts(StorageType storageType) {
        var accounts = GetAccounts(storageType);
        if (!accounts.Any()) {
            throw new Exception($"No Storage Accounts for {storageType}");
        }

        var account_list = accounts.ToList();
        if (account_list.Count == 1) {
            return account_list[0];
        }

        // Use a random secondary storage account if any are available.  This
        // reduces IOP contention for the Storage Queues, which are only available
        // on primary accounts
        //
        // security note: this is not used as a security feature
        var random = new Random();
        var index = random.Next(account_list.Count);

        return account_list[index];  // nosec
    }

    public IEnumerable<string> GetAccounts(StorageType storageType) {
        switch (storageType) {
            case StorageType.Corpus:
                return CorpusAccounts();
            case StorageType.Config:
                return new[] { GetFuncStorage() };
            default:
                throw new NotImplementedException();
        }
    }

    public Uri GetTableEndpoint(string accountId)
        => new($"https://{accountId}.table.core.windows.net/");

    public Uri GetQueueEndpoint(string accountId)
        => new($"https://{accountId}.queue.core.windows.net/");

    public Uri GetBlobEndpoint(string accountId)
        => new($"https://{accountId}.blob.core.windows.net/");
}
