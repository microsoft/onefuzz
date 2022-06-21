using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.OneFuzz.Service;

using Async = System.Threading.Tasks;

namespace Tests.Integration;

// This exists solely to allow use of a fixed storage account in integration tests
// against live Azure Storage.
sealed class AzureStorage : IStorage {
    public static IStorage FromEnvironment() {
        var accountName = Environment.GetEnvironmentVariable("AZURE_ACCOUNT_NAME");
        var accountKey = Environment.GetEnvironmentVariable("AZURE_ACCOUNT_KEY");

        if (accountName is null) {
            throw new Exception("AZURE_ACCOUNT_NAME must be set in environment to run integration tests (use --filter 'Category!=Integration' to skip them)");
        }

        if (accountKey is null) {
            throw new Exception("AZURE_ACCOUNT_KEY must be set in environment to run integration tests (use --filter 'Category!=Integration' to skip them)");
        }

        return new AzureStorage(accountName, accountKey);
    }

    public string AccountName { get; }
    public string AccountKey { get; }

    public AzureStorage(string accountName, string accountKey) {
        AccountName = accountName;
        AccountKey = accountKey;
    }

    public IEnumerable<string> CorpusAccounts() {
        throw new System.NotImplementedException();
    }

    public IEnumerable<string> GetAccounts(StorageType storageType) {
        throw new System.NotImplementedException();
    }

    public string GetPrimaryAccount(StorageType storageType) {
        throw new System.NotImplementedException();
    }

    public Task<(string, string)> GetStorageAccountNameAndKey(string accountId)
        => Async.Task.FromResult((AccountName, AccountKey));

    public Task<string?> GetStorageAccountNameAndKeyByName(string accountName) {
        throw new System.NotImplementedException();
    }

    public Uri GetTableEndpoint(string accountId)
        => new($"https://{AccountName}.table.core.windows.net/");

    public Uri GetQueueEndpoint(string accountId)
        => new($"https://{AccountName}.queue.core.windows.net/");

    public Uri GetBlobEndpoint(string accountId)
        => new($"https://{AccountName}.blob.core.windows.net/");
}
