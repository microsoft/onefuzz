using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Data.Tables;
using Azure.ResourceManager.Storage;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Storage.Sas;
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

    public BlobServiceClient GetBlobServiceClientForAccountName(string accountName) {
        var cred = new StorageSharedKeyCredential(AccountName, AccountKey);
        return new BlobServiceClient(BlobEndpoint, cred);
    }

    public BlobServiceClient GetBlobServiceClientForAccount(ResourceIdentifier accountId)
        => GetBlobServiceClientForAccountName(accountId.Name);

    public TableServiceClient GetTableServiceClientForAccount(ResourceIdentifier accountId) {
        var cred = new TableSharedKeyCredential(AccountName, AccountKey);
        return new TableServiceClient(TableEndpoint, cred);
    }

    private static readonly QueueClientOptions _queueClientOptions = new() { MessageEncoding = QueueMessageEncoding.Base64 };
    private QueueServiceClient GetQueueServiceClientForAccountName(string accountName) {
        var cred = new StorageSharedKeyCredential(AccountName, AccountKey);
        return new QueueServiceClient(QueueEndpoint, cred, _queueClientOptions);
    }

    public QueueServiceClient GetQueueServiceClientForAccount(ResourceIdentifier accountId)
        => GetQueueServiceClientForAccountName(accountId.Name);

    IReadOnlyList<ResourceIdentifier> IStorage.CorpusAccounts() {
        throw new NotImplementedException();
    }

    public ResourceIdentifier GetPrimaryAccount(StorageType storageType) {
        throw new System.NotImplementedException();
    }

    public Task<Uri> GenerateBlobContainerSasUri(
        BlobContainerSasPermissions permissions,
        BlobContainerClient containerClient,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow)
        => Async.Task.FromResult(containerClient.GenerateSasUri(new BlobSasBuilder(permissions, timeWindow.endTime) {
            StartsOn = timeWindow.startTime,
        }));

    public Task<Uri> GenerateBlobSasUri(
        BlobSasPermissions permissions,
        BlobClient blobClient,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow)
        => Async.Task.FromResult(blobClient.GenerateSasUri(new BlobSasBuilder(permissions, timeWindow.endTime) {
            StartsOn = timeWindow.startTime,
        }));

    public Task<Uri> GenerateQueueSasUri(
        QueueSasPermissions permissions,
        string accountName,
        string queueName,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow) {
        var client = GetQueueServiceClientForAccountName(accountName).GetQueueClient(queueName);
        return Async.Task.FromResult(client.GenerateSasUri(new QueueSasBuilder(permissions, timeWindow.endTime) {
            StartsOn = timeWindow.startTime,
        }));
    }
}
