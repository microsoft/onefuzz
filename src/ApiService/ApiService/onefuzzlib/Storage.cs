using System.Collections.Generic;
using System;
using Azure.ResourceManager;
using Azure.ResourceManager.Storage;
using Azure.Core;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Microsoft.OneFuzz.Service;

public interface IStorage {
    public ArmClient GetMgmtClient();

    public IEnumerable<string> CorpusAccounts();
}

public class Storage : IStorage {

    private ICreds _creds;
    private readonly ILogger _logger;

    public Storage(ILoggerFactory loggerFactory, ICreds creds) {
        _creds = creds;
        _logger = loggerFactory.CreateLogger<Storage>();
    }

    // TODO: @cached
    public static string GetFuncStorage() {
        return EnvironmentVariables.OneFuzz.FuncStorage
            ?? throw new Exception("Func storage env var is missing");
    }

    // TODO: @cached
    public static string GetFuzzStorage() {
        return EnvironmentVariables.OneFuzz.DataStorage
            ?? throw new Exception("Fuzz storage env var is missing");
    }

    // TODO: @cached
    public ArmClient GetMgmtClient() {
        return new ArmClient(credential: _creds.GetIdentity(), defaultSubscriptionId: _creds.GetSubcription());
    }

    // TODO: @cached
    public IEnumerable<string> CorpusAccounts() {
        var skip = GetFuncStorage();
        var results = new List<string> {GetFuzzStorage()};

        var client = GetMgmtClient();
        var group = new ResourceIdentifier(_creds.GetBaseResourceGroup());

        const string storageTypeTagKey = "storage_type";

        var resourceGroup = client.GetResourceGroup(group);
        foreach (var account in resourceGroup.GetStorageAccounts()) {
            if (account.Id == skip) {
                continue;
            }

            if (results.Contains(account.Id)) {
                continue;
            }

            if (string.IsNullOrEmpty(account.Data.PrimaryEndpoints.Blob)) {
                continue;
            }

            if (!account.Data.Tags.ContainsKey(storageTypeTagKey)
                || account.Data.Tags[storageTypeTagKey] != "corpus") {
                continue;
            }

            results.Add(account.Id);
        }

        _logger.LogInformation($"corpus accounts: {JsonSerializer.Serialize(results)}");
        return results;
    }
}
