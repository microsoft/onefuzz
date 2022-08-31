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

    public Task<BlobServiceClient> GetBlobServiceClientForAccountName(string accountName) {
        var cred = new StorageSharedKeyCredential(AccountName, AccountKey);
        return Async.Task.FromResult(new BlobServiceClient(_blobEndpoint, cred));
    }

    public Task<TableServiceClient> GetTableServiceClientForAccountName(string accountName) {
        var cred = new TableSharedKeyCredential(AccountName, AccountKey);
        return Async.Task.FromResult(new TableServiceClient(_tableEndpoint, cred));
    }

    private static readonly QueueClientOptions _queueClientOptions = new() { MessageEncoding = QueueMessageEncoding.Base64 };
    public Task<QueueServiceClient> GetQueueServiceClientForAccountName(string accountName) {
        var cred = new StorageSharedKeyCredential(AccountName, AccountKey);
        return Async.Task.FromResult(new QueueServiceClient(_queueEndpoint, cred, _queueClientOptions));
    }

    public IReadOnlyList<ResourceIdentifier> GetAccounts(StorageType storageType) {
        return new[] { _fakeResourceIdentifier };
    }

    public IReadOnlyList<ResourceIdentifier> CorpusAccounts() {
        throw new System.NotImplementedException();
    }

    public ResourceIdentifier GetPrimaryAccount(StorageType storageType) => _fakeResourceIdentifier;

    public async Task<Uri> GenerateQueueSasUri(
        QueueSasPermissions permissions,
        string accountId,
        string queueName,
        (DateTimeOffset startTime, DateTimeOffset endTime) timeWindow) {
        var accountClient = await GetQueueServiceClientForAccountName(accountId);
        var client = accountClient.GetQueueClient(queueName);
        return client.GenerateSasUri(new QueueSasBuilder(permissions, timeWindow.endTime) {
            StartsOn = timeWindow.startTime,
        });
    }
}
