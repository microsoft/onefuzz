using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Data.Tables;
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

    public IReadOnlyList<string> GetAccounts(StorageType storageType) {
        return new[] { AccountName };
    }


    public Task<string?> GetStorageAccountNameKeyByName(string accountName) {
        return Async.Task.FromResult<string?>(AccountName);
    }

    public Uri GetTableEndpoint(string accountId)
        => new($"https://{AccountName}.table.core.windows.net/");

    public Uri GetQueueEndpoint(string accountId)
        => new($"https://{AccountName}.queue.core.windows.net/");

    public Uri GetBlobEndpoint(string accountId)
        => new($"https://{AccountName}.blob.core.windows.net/");

    public BlobServiceClient GetBlobServiceClientForAccount(string accountId) {
        var cred = new StorageSharedKeyCredential(AccountName, AccountKey);
        return new BlobServiceClient(GetBlobEndpoint(accountId), cred);
    }

    public TableServiceClient GetTableServiceClientForAccount(string accountId) {
        var cred = new TableSharedKeyCredential(AccountName, AccountKey);
        return new TableServiceClient(GetTableEndpoint(accountId), cred);
    }

    private static readonly QueueClientOptions _queueClientOptions = new() { MessageEncoding = QueueMessageEncoding.Base64 };
    public QueueServiceClient GetQueueServiceClientForAccount(string accountId) {
        var cred = new StorageSharedKeyCredential(AccountName, AccountKey);
        return new QueueServiceClient(GetQueueEndpoint(accountId), cred, _queueClientOptions);
    }

    IReadOnlyList<string> IStorage.CorpusAccounts() {
        throw new NotImplementedException();
    }

    public IReadOnlyList<string> CorpusAccounts() {
        throw new System.NotImplementedException();
    }

    public string GetPrimaryAccount(StorageType storageType) {
        throw new System.NotImplementedException();
    }

    public Task<string?> GetStorageAccountNameAndKeyByName(string accountName) {
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
        string accountId,
        string queueName,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow) {
        var client = GetQueueServiceClientForAccount(accountId).GetQueueClient(queueName);
        return Async.Task.FromResult(client.GenerateSasUri(new QueueSasBuilder(permissions, timeWindow.endTime) {
            StartsOn = timeWindow.startTime,
        }));
    }
}
