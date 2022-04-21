using System.Collections.Generic;
using System;
using Azure.ResourceManager;
using Azure.ResourceManager.Storage;
using Azure.Core;
using System.Text.Json;
using System.Linq;

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
    public List<string> GetAccounts(StorageType storageType);
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

    public string ChooseAccounts(StorageType storageType)
    {
        var accounts = GetAccounts(storageType);
        if (!accounts.Any())
        {
            throw new Exception($"No Storage Accounts for {storageType}");
        }

        if (accounts.Count == 1)
        {
            return accounts[0];
        }

        // Use a random secondary storage account if any are available.  This
        // reduces IOP contention for the Storage Queues, which are only available
        // on primary accounts
        //
        // security note: this is not used as a security feature
        var random = new Random();
        var index = random.Next(accounts.Count);

        return accounts[index];  // nosec
    }
    public List<string> GetAccounts(StorageType storageType)
    {
        if (storageType == StorageType.Corpus)
        {
            return CorpusAccounts().ToList();
        } else if(storageType == StorageType.Config)
        {
            return new List<string> { GetFuncStorage() };
        } else {
            throw new NotImplementedException();
        }
    }
}
