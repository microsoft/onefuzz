using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using ApiService.OneFuzzLib.Orm;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using IntegrationTests.Fakes;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Xunit;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace IntegrationTests;

// FunctionTestBase contains shared implementations for running
// functions against live Azure Storage or the Azurite emulator.
// 
// To use this base class, derive an abstract class from this
// with all the tests defined in it. Then, from that class
// derive two non-abstract classes for XUnit to find:
// - one for Azurite
// - one for Azure Storage (marked with [Trait("Category", "Live")])
//
// See AgentEventsTests for an example.
public abstract class FunctionTestBase : IAsyncLifetime {
    private readonly IStorage _storage;

    // each test will use a different prefix for storage (tables, blobs) so they don't interfere
    // with each other - generate a prefix like t12345678 (table names must start with letter)
    private readonly string _storagePrefix = "t" + Guid.NewGuid().ToString()[..8];

    private readonly Guid _subscriptionId = Guid.NewGuid();
    private readonly string _resourceGroup = "FakeResourceGroup";
    private readonly Region _region = Region.Parse("fakeregion");

    protected ILogger Logger { get; }

    protected TestContext Context { get; }

    protected OneFuzzLoggerProvider LoggerProvider { get; }

    private readonly BlobServiceClient _blobClient;
    protected BlobContainerClient GetContainerClient(Container container)
        => _blobClient.GetBlobContainerClient(_storagePrefix + container.String);

    public FunctionTestBase(ITestOutputHelper output, IStorage storage) {
        var instanceId = Guid.NewGuid().ToString();

        LoggerProvider = new OneFuzzLoggerProvider(output);
        Logger = LoggerProvider.CreateLogger("Test");
        _storage = storage;


        var creds = new TestCreds(_subscriptionId, _resourceGroup, _region, instanceId);
        Context = new TestContext(new DefaultHttpClientFactory(), LoggerProvider, _storage, creds, _storagePrefix);

        // set up blob client for test purposes:
        // this is always sync for test purposes
        _blobClient = _storage.GetBlobServiceClientForAccount(_storage.GetPrimaryAccount(StorageType.Config)).Result;

        var baseConfigContainer = WellKnownContainers.BaseConfig;
        var containerClient = GetContainerClient(baseConfigContainer);
        _ = containerClient.Create();
        _ = containerClient.GetBlobClient("instance_id").Upload(new BinaryData(instanceId));

        _ = GetContainerClient(WellKnownContainers.Events).Create();
    }

    public async Task InitializeAsync() {
        await Program.SetupStorage(Context.Storage, Context.ServiceConfiguration);
    }

    public async Task DisposeAsync() {
        // clean up any tables & blobs that this test created
        var account = _storage.GetPrimaryAccount(StorageType.Config);
        await Task.WhenAll(
            CleanupTables(await _storage.GetTableServiceClientForAccount(account)),
            CleanupBlobs(await _storage.GetBlobServiceClientForAccount(account)));
    }

    protected static string BodyAsString(HttpResponseData data) {
        _ = data.Body.Seek(0, SeekOrigin.Begin);
        using var sr = new StreamReader(data.Body);
        return sr.ReadToEnd();
    }

    protected static T BodyAs<T>(HttpResponseData data)
        => EntityConverter.FromJsonString<T>(BodyAsString(data)) ?? throw new Exception($"unable to deserialize body as {typeof(T)}");

    private async Task CleanupBlobs(BlobServiceClient blobClient)
    => await Task.WhenAll(
        await blobClient
            .GetBlobContainersAsync(prefix: _storagePrefix)
            .Where(c => c.IsDeleted != true)
            .Select(async container => {
                try {
                    using var _ = await blobClient.DeleteBlobContainerAsync(container.Name);
                    Logger.LogInformation("cleaned up container {ContainerName}", container.Name);
                } catch (Exception ex) {
                    // swallow any exceptions: this is a best-effort attempt to cleanup
                    Logger.LogError(ex, "error deleting container {ContainerName} at end of test", container.Name);
                }
            })
            .ToListAsync());

    private async Task CleanupTables(TableServiceClient tableClient)
        => await Task.WhenAll(
            await tableClient
                .QueryAsync(filter: Query.StartsWith("TableName", _storagePrefix))
                .Select(async table => {
                    try {
                        using var _ = await tableClient.DeleteTableAsync(table.Name);
                        Logger.LogInformation("cleaned up table {TableName}", table.Name);
                    } catch (Exception ex) {
                        // swallow any exceptions: this is a best-effort attempt to cleanup
                        Logger.LogError(ex, "error deleting table {TableName} at end of test", table.Name);
                    }
                })
                .ToListAsync());
}

public sealed class DefaultHttpClientFactory : IHttpClientFactory, IDisposable {
    private readonly Lazy<HttpMessageHandler> _handlerLazy = new(() => new HttpClientHandler());

    public HttpClient CreateClient(string name) => new(_handlerLazy.Value, disposeHandler: false);

    public void Dispose() {
        if (_handlerLazy.IsValueCreated) {
            _handlerLazy.Value.Dispose();
        }
    }
}
