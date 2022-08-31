using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Data.Tables;
using Azure.ResourceManager.Storage;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.OneFuzz.Service;

using Async = System.Threading.Tasks;

namespace IntegrationTests.Integration;

// This exists solely to allow use of a fixed storage account in integration tests
// against live Azure Storage.
sealed class AzureStorage : IStorage {
    public static IStorage FromEnvironment() {
        var accountName = Environment.GetEnvironmentVariable("AZURE_ACCOUNT_NAME");
        var accountKey = Environment.GetEnvironmentVariable("AZURE_ACCOUNT_KEY");

        if (accountName is null) {
            throw new Exception("AZURE_ACCOUNT_NAME must be set in environment to run integration tests (use --filter 'Category!=Live' to skip them)");
        }

        if (accountKey is null) {
            throw new Exception("AZURE_ACCOUNT_KEY must be set in environment to run integration tests (use --filter 'Category!=Live' to skip them)");
        }

        return new AzureStorage(accountName, accountKey);
    }

    public string AccountName { get; }
    public string AccountKey { get; }

    public AzureStorage(string accountName, string accountKey) {
        AccountName = accountName;
        AccountKey = accountKey;
    }

    private readonly string _fakeSubscription = Guid.NewGuid().ToString();

    private ResourceIdentifier _fakeResourceIdentifier =>
        StorageAccountResource.CreateResourceIdentifier(_fakeSubscription, "unused", AccountName);

    public IReadOnlyList<ResourceIdentifier> GetAccounts(StorageType storageType) {
        return new[] { _fakeResourceIdentifier };
    }

    private Uri TableEndpoint => new($"https://{AccountName}.table.core.windows.net/");

    private Uri QueueEndpoint => new($"https://{AccountName}.queue.core.windows.net/");

    private Uri BlobEndpoint => new($"https://{AccountName}.blob.core.windows.net/");

    public Task<BlobServiceClient> GetBlobServiceClientForAccountName(string accountName) {
        var cred = new StorageSharedKeyCredential(AccountName, AccountKey);
        return Async.Task.FromResult(new BlobServiceClient(BlobEndpoint, cred));
    }

    public Task<TableServiceClient> GetTableServiceClientForAccountName(string accountName) {
        var cred = new TableSharedKeyCredential(AccountName, AccountKey);
        return Async.Task.FromResult(new TableServiceClient(TableEndpoint, cred));
    }

    private static readonly QueueClientOptions _queueClientOptions = new() { MessageEncoding = QueueMessageEncoding.Base64 };
    public Task<QueueServiceClient> GetQueueServiceClientForAccountName(string accountName) {
        var cred = new StorageSharedKeyCredential(AccountName, AccountKey);
        return Async.Task.FromResult(new QueueServiceClient(QueueEndpoint, cred, _queueClientOptions));
    }

    IReadOnlyList<ResourceIdentifier> IStorage.CorpusAccounts() {
        throw new NotImplementedException();
    }

    public ResourceIdentifier GetPrimaryAccount(StorageType storageType) {
        throw new System.NotImplementedException();
    }
}
