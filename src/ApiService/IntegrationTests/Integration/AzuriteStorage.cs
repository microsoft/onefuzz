using System;
using System.Collections.Generic;
using System.Net.Http;
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

// An implementation of IStorage for communicating with the Azurite Storage emulator.
sealed class AzuriteStorage : IStorage {
    static AzuriteStorage() {
        try {
            using var client = new HttpClient();
            client.GetAsync(new Uri("http://127.0.0.1:10000")).Wait();
        } catch {
            Console.Error.WriteLine("'azurite' must be running to run integration tests.");
            Environment.Exit(1);
        }
    }

    private static readonly Uri _blobEndpoint = new($"http://127.0.0.1:10000/devstoreaccount1");

    private static readonly Uri _queueEndpoint = new($"http://127.0.0.1:10001/devstoreaccount1");

    private static readonly Uri _tableEndpoint = new($"http://127.0.0.1:10002/devstoreaccount1");

    // This is the fixed name & account key used by Azurite (derived from devstorage emulator);
    // https://docs.microsoft.com/en-us/azure/storage/common/storage-configure-connection-string#configure-a-connection-string-for-azurite
    const string AccountName = "devstoreaccount1";
    const string AccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
    private static readonly ResourceIdentifier _fakeResourceIdentifier = StorageAccountResource.CreateResourceIdentifier(Guid.NewGuid().ToString(), "unused", AccountName);

    public BlobServiceClient GetBlobServiceClientForAccountName(string accountName) {
        var cred = new StorageSharedKeyCredential(AccountName, AccountKey);
        return new BlobServiceClient(_blobEndpoint, cred);
    }

    public BlobServiceClient GetBlobServiceClientForAccount(ResourceIdentifier accountId)
        => GetBlobServiceClientForAccountName(accountId.Name);

    public TableServiceClient GetTableServiceClientForAccount(ResourceIdentifier accountId) {
        var cred = new TableSharedKeyCredential(AccountName, AccountKey);
        return new TableServiceClient(_tableEndpoint, cred);
    }

    private static readonly QueueClientOptions _queueClientOptions = new() { MessageEncoding = QueueMessageEncoding.Base64 };
    private static QueueServiceClient GetQueueServiceClientForAccountName(string accountName) {
        var cred = new StorageSharedKeyCredential(AccountName, AccountKey);
        return new QueueServiceClient(_queueEndpoint, cred, _queueClientOptions);
    }
    public QueueServiceClient GetQueueServiceClientForAccount(ResourceIdentifier accountId)
        => GetQueueServiceClientForAccountName(accountId.Name);

    public IReadOnlyList<ResourceIdentifier> GetAccounts(StorageType storageType) {
        return new[] { _fakeResourceIdentifier };
    }

    public IReadOnlyList<ResourceIdentifier> CorpusAccounts() {
        throw new System.NotImplementedException();
    }

    public ResourceIdentifier GetPrimaryAccount(StorageType storageType) => _fakeResourceIdentifier;

    public Task<Uri> GenerateBlobContainerSasUri(
        BlobContainerSasPermissions permissions,
        BlobContainerClient containerClient,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow) {
        return Async.Task.FromResult(containerClient.GenerateSasUri(permissions, timeWindow.endTime));
    }

    public Task<Uri> GenerateBlobSasUri(
        BlobSasPermissions permissions,
        BlobClient blobClient,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow) {
        return Async.Task.FromResult(blobClient.GenerateSasUri(permissions, timeWindow.endTime));
    }

    public Task<Uri> GenerateQueueSasUri(
        QueueSasPermissions permissions,
        string accountId,
        string queueName,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow) {
        var client = GetQueueServiceClientForAccountName(accountId).GetQueueClient(queueName);
        return Async.Task.FromResult(client.GenerateSasUri(new QueueSasBuilder(permissions, timeWindow.endTime) {
            StartsOn = timeWindow.startTime,
        }));
    }
}
