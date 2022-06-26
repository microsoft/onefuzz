using System;
using System.IO;
using System.Linq;
using ApiService.OneFuzzLib.Orm;
using Azure.Data.Tables;
using Azure.Storage;
using Azure.Storage.Blobs;
using IntegrationTests.Fakes;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Xunit.Abstractions;

namespace IntegrationTests;

// FunctionTestBase contains shared implementations for running
// functions against live Azure Storage or the Azurite emulator.
// 
// To use this base class, derive an abstract class from this
// with all the tests defined in it. Then, from that class
// derive two non-abstract classes for XUnit to find:
// - one for Azurite
// - one for Azure Storage (marked with [Trait("Category", "Integration")])
//
// See AgentEventsTests for an example.
public abstract class FunctionTestBase : IDisposable {
    private readonly IStorage _storage;

    // each test will use a different prefix for storage (tables, blobs) so they don't interfere
    // with each other - generate a prefix like t12345678 (table names must start with letter)
    private readonly string _storagePrefix = "t" + Guid.NewGuid().ToString()[..8];

    private readonly Guid _subscriptionId = Guid.NewGuid();
    private readonly string _resourceGroup = "FakeResourceGroup";
    private readonly string _region = "fakeregion";

    protected ILogTracer Logger { get; }

    protected TestContext Context { get; }

    private readonly BlobServiceClient _blobClient;
    protected BlobContainerClient GetContainerClient(string container)
        => _blobClient.GetBlobContainerClient(_storagePrefix + container);

    public FunctionTestBase(ITestOutputHelper output, IStorage storage) {
        Logger = new TestLogTracer(output);
        _storage = storage;

        var creds = new TestCreds(_subscriptionId, _resourceGroup, _region);
        Context = new TestContext(Logger, _storage, creds, _storagePrefix);

        // set up blob client for test purposes:
        var (accountName, accountKey) = _storage.GetStorageAccountNameAndKey("").Result; // for test impls this is always sync
        var endpoint = _storage.GetBlobEndpoint("");
        _blobClient = new BlobServiceClient(endpoint, new StorageSharedKeyCredential(accountName, accountKey));
    }

    protected static string BodyAsString(HttpResponseData data) {
        data.Body.Seek(0, SeekOrigin.Begin);
        using var sr = new StreamReader(data.Body);
        return sr.ReadToEnd();
    }

    protected static T BodyAs<T>(HttpResponseData data)
        => new EntityConverter().FromJsonString<T>(BodyAsString(data)) ?? throw new Exception($"unable to deserialize body as {typeof(T)}");

    public void Dispose() {
        var (accountName, accountKey) = _storage.GetStorageAccountNameAndKey("").Result; // sync for test impls
        if (accountName is not null && accountKey is not null) {
            // clean up any tables & blobs that this test created

            CleanupTables(_storage.GetTableEndpoint(accountName),
                new TableSharedKeyCredential(accountName, accountKey));

            CleanupBlobs(_storage.GetBlobEndpoint(accountName),
                new StorageSharedKeyCredential(accountName, accountKey));
        }
    }

    private void CleanupBlobs(Uri endpoint, StorageSharedKeyCredential creds) {
        var blobClient = new BlobServiceClient(endpoint, creds);

        var containersToDelete = blobClient.GetBlobContainers(prefix: _storagePrefix);
        foreach (var container in containersToDelete.Where(c => c.IsDeleted != true)) {
            try {
                blobClient.DeleteBlobContainer(container.Name);
                Logger.Info($"cleaned up container {container.Name}");
            } catch (Exception ex) {
                // swallow any exceptions: this is a best-effort attempt to cleanup
                Logger.Exception(ex, "error deleting container at end of test");
            }
        }
    }

    private void CleanupTables(Uri endpoint, TableSharedKeyCredential creds) {
        var tableClient = new TableServiceClient(endpoint, creds);

        var tablesToDelete = tableClient.Query(filter: Query.StartsWith("TableName", _storagePrefix));
        foreach (var table in tablesToDelete) {
            try {
                tableClient.DeleteTable(table.Name);
                Logger.Info($"cleaned up table {table.Name}");
            } catch (Exception ex) {
                // swallow any exceptions: this is a best-effort attempt to cleanup
                Logger.Exception(ex, "error deleting table at end of test");
            }
        }
    }
}
