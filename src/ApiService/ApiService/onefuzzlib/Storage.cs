using Azure.ResourceManager;
using Azure.ResourceManager.Storage;
using Azure.Core;
using System.Text.Json;

namespace Microsoft.OneFuzz.Service;

public enum StorageType
{
    Corpus,
    Config
}

public interface IStorage
{
    public ArmClient GetMgmtClient();

    public IEnumerable<string> CorpusAccounts();
    string GetPrimaryAccount(StorageType storageType);
    public (string?, string?) GetStorageAccountNameAndKey(string accountId);

    public IEnumerable<string> GetAccounts(StorageType storageType);
}

public class Storage : IStorage
{
    private ICreds _creds;
    private ArmClient _armClient;
    private ILogTracer _log;

    public Storage(ICreds creds, ILogTracer log)
    {
        _creds = creds;
        _armClient = new ArmClient(credential: _creds.GetIdentity(), defaultSubscriptionId: _creds.GetSubcription());
        _log = log;
    }

    public static string GetFuncStorage()
    {
        return EnvironmentVariables.OneFuzz.FuncStorage
            ?? throw new Exception("Func storage env var is missing");
    }

    public static string GetFuzzStorage()
    {
        return EnvironmentVariables.OneFuzz.DataStorage
            ?? throw new Exception("Fuzz storage env var is missing");
    }

    public ArmClient GetMgmtClient()
    {
        return _armClient;
    }

    // TODO: @cached
    public IEnumerable<string> CorpusAccounts()
    {
        var skip = GetFuncStorage();
        var results = new List<string> { GetFuzzStorage() };

        var client = GetMgmtClient();
        var group = _creds.GetResourceGroupResourceIdentifier();

        const string storageTypeTagKey = "storage_type";

        var resourceGroup = client.GetResourceGroupResource(group);
        foreach (var account in resourceGroup.GetStorageAccounts())
        {
            if (account.Id == skip)
            {
                continue;
            }

            if (results.Contains(account.Id!))
            {
                continue;
            }

            if (string.IsNullOrEmpty(account.Data.PrimaryEndpoints.Blob))
            {
                continue;
            }

            if (!account.Data.Tags.ContainsKey(storageTypeTagKey)
                || account.Data.Tags[storageTypeTagKey] != "corpus")
            {
                continue;
            }

            results.Add(account.Id!);
        }

        _log.Info($"corpus accounts: {JsonSerializer.Serialize(results)}");
        return results;
    }

    public string GetPrimaryAccount(StorageType storageType)
    {
        return
            storageType switch
            {
                StorageType.Corpus => GetFuzzStorage(),
                StorageType.Config => GetFuncStorage(),
                _ => throw new NotImplementedException(),
            };
    }

    public (string?, string?) GetStorageAccountNameAndKey(string accountId)
    {
        var resourceId = new ResourceIdentifier(accountId);
        var armClient = GetMgmtClient();
        var storageAccount = armClient.GetStorageAccountResource(resourceId);
        var key = storageAccount.GetKeys().Value.Keys.FirstOrDefault();
        return (resourceId.Name, key?.Value);
    }

    public IEnumerable<string> GetAccounts(StorageType storageType)
    {
        switch (storageType)
        {
            case StorageType.Corpus:
                return CorpusAccounts();
            case StorageType.Config:
                return new[] { GetFuncStorage() };
            default:
                throw new NotImplementedException();
        }
    }

    public void GetStorageAccountNameKey(string accountId)
    {

    }
}
