using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.ResourceManager;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace Microsoft.OneFuzz.Service;


public interface IContainers {
    public Async.Task<BinaryData?> GetBlob(Container container, string name, StorageType storageType);

    public Async.Task<Uri?> CreateContainer(Container container, StorageType storageType, IDictionary<string, string>? metadata);

    public Async.Task<BlobContainerClient?> GetOrCreateContainerClient(Container container, StorageType storageType, IDictionary<string, string>? metadata);

    public Async.Task<BlobContainerClient?> FindContainer(Container container, StorageType storageType);

    public Async.Task<Uri> GetFileSasUrl(Container container, string name, StorageType storageType, BlobSasPermissions permissions, TimeSpan? duration = null);
    public Async.Task SaveBlob(Container container, string name, string data, StorageType storageType);
    public Async.Task<Guid> GetInstanceId();

    public Async.Task<Uri?> GetFileUrl(Container container, string name, StorageType storageType);

    public Async.Task<Uri> GetContainerSasUrl(Container container, StorageType storageType, BlobContainerSasPermissions permissions, TimeSpan? duration = null);

    public Async.Task<bool> BlobExists(Container container, string name, StorageType storageType);

    public Async.Task<Uri> AddContainerSasUrl(Uri uri, TimeSpan? duration = null);
    public Async.Task<Dictionary<string, IDictionary<string, string>>> GetContainers(StorageType corpus);
}

public class Containers : IContainers {
    private ILogTracer _log;
    private IStorage _storage;
    private ICreds _creds;
    private ArmClient _armClient;
    private readonly IServiceConfig _config;

    static TimeSpan CONTAINER_SAS_DEFAULT_DURATION = TimeSpan.FromDays(30);

    public Containers(ILogTracer log, IStorage storage, ICreds creds, IServiceConfig config) {
        _log = log;
        _storage = storage;
        _creds = creds;
        _armClient = creds.ArmClient;
        _config = config;

        _getInstanceId = new Lazy<Async.Task<Guid>>(async () => {
            var blob = await GetBlob(new Container("base-config"), "instance_id", StorageType.Config);
            if (blob == null) {
                throw new Exception("Blob Not Found");
            }

            return Guid.Parse(blob.ToString());
        }, LazyThreadSafetyMode.PublicationOnly);
    }

    public async Async.Task<Uri?> GetFileUrl(Container container, string name, StorageType storageType) {
        var client = await FindContainer(container, storageType);
        if (client is null)
            return null;

        return new Uri($"{_storage.GetBlobEndpoint(client.AccountName)}{container}/{name}");
    }

    public async Async.Task<BinaryData?> GetBlob(Container container, string name, StorageType storageType) {
        var client = await FindContainer(container, storageType);

        if (client == null) {
            return null;
        }

        try {
            return (await client.GetBlobClient(name).DownloadContentAsync())
                .Value.Content;
        } catch (RequestFailedException) {
            return null;
        }
    }

    public async Task<Uri?> CreateContainer(Container container, StorageType storageType, IDictionary<string, string>? metadata) {
        var client = await GetOrCreateContainerClient(container, storageType, metadata);
        if (client is null) {
            return null;
        }

        return GetContainerSasUrlService(client, _containerCreatePermissions);
    }

    private static readonly BlobContainerSasPermissions _containerCreatePermissions
        = BlobContainerSasPermissions.Read
        | BlobContainerSasPermissions.Write
        | BlobContainerSasPermissions.Delete
        | BlobContainerSasPermissions.List;

    public async Task<BlobContainerClient?> GetOrCreateContainerClient(Container container, StorageType storageType, IDictionary<string, string>? metadata) {
        var containerClient = await FindContainer(container, StorageType.Corpus);
        if (containerClient is not null) {
            return containerClient;
        }

        var account = _storage.ChooseAccount(storageType);
        var client = await _storage.GetBlobServiceClientForAccount(account);
        var containerName = _config.OneFuzzStoragePrefix + container.ContainerName;
        var cc = client.GetBlobContainerClient(containerName);
        try {
            await cc.CreateAsync(metadata: metadata);
        } catch (RequestFailedException ex) when (ex.ErrorCode == "ContainerAlreadyExists") {
            // note: resource exists error happens during creation if the container
            // is being deleted
            _log.Error($"unable to create container.  account: {account} container: {container.ContainerName} metadata: {metadata} - {ex.Message}");
            return null;
        }

        return cc;
    }


    public async Async.Task<BlobContainerClient?> FindContainer(Container container, StorageType storageType) {
        // # check secondary accounts first by searching in reverse.
        // #
        // # By implementation, the primary account is specified first, followed by
        // # any secondary accounts.
        // #
        // # Secondary accounts, if they exist, are preferred for containers and have
        // # increased IOP rates, this should be a slight optimization

        var containerName = _config.OneFuzzStoragePrefix + container.ContainerName;

        var containers =
            _storage.GetAccounts(storageType)
            .Reverse()
            .Select(async account => (await _storage.GetBlobServiceClientForAccount(account)).GetBlobContainerClient(containerName));

        foreach (var c in containers) {
            var client = await c;
            if ((await client.ExistsAsync()).Value) {
                return client;
            }
        }
        return null;
    }

    public async Async.Task<Uri> GetFileSasUrl(Container container, string name, StorageType storageType, BlobSasPermissions permissions, TimeSpan? duration = null) {
        var client = await FindContainer(container, storageType) ?? throw new Exception($"unable to find container: {container.ContainerName} - {storageType}");

        var (startTime, endTime) = SasTimeWindow(duration ?? TimeSpan.FromDays(30));

        var sasBuilder = new BlobSasBuilder(permissions, endTime) {
            StartsOn = startTime,
            BlobContainerName = _config.OneFuzzStoragePrefix + container.ContainerName,
            BlobName = name
        };

        var sasUrl = client.GetBlobClient(name).GenerateSasUri(sasBuilder);
        return sasUrl;
    }

    public static (DateTimeOffset, DateTimeOffset) SasTimeWindow(TimeSpan timeSpan) {
        // SAS URLs are valid 6 hours earlier, primarily to work around dev
        // workstations having out-of-sync time.  Additionally, SAS URLs are stopped
        // 15 minutes later than requested based on "Be careful with SAS start time"
        // guidance.
        // Ref: https://docs.microsoft.com/en-us/azure/storage/common/storage-sas-overview

        var SAS_START_TIME_DELTA = TimeSpan.FromHours(6);
        var SAS_END_TIME_DELTA = TimeSpan.FromMinutes(6);

        var now = DateTimeOffset.UtcNow;
        var start = now - SAS_START_TIME_DELTA;
        var expiry = now + timeSpan + SAS_END_TIME_DELTA;
        return (start, expiry);
    }

    public async Async.Task SaveBlob(Container container, string name, string data, StorageType storageType) {
        var client = await FindContainer(container, storageType) ?? throw new Exception($"unable to find container: {container.ContainerName} - {storageType}");
        await client.GetBlobClient(name).UploadAsync(new BinaryData(data), overwrite: true);
    }

    public virtual Async.Task<Guid> GetInstanceId() => _getInstanceId.Value;
    private readonly Lazy<Async.Task<Guid>> _getInstanceId;

    public static Uri? GetContainerSasUrlService(
        BlobContainerClient client,
        BlobContainerSasPermissions permissions,
        bool tag = false,
        TimeSpan? timeSpan = null) {
        var (start, expiry) = SasTimeWindow(timeSpan ?? TimeSpan.FromDays(30.0));
        var sasBuilder = new BlobSasBuilder(permissions, expiry) { StartsOn = start };
        return client.GenerateSasUri(sasBuilder);
    }

    public async Async.Task<Uri> AddContainerSasUrl(Uri uri, TimeSpan? duration = null) {
        if (uri.Query.Contains("sig")) {
            return uri;
        }

        var (startTime, endTime) = SasTimeWindow(duration ?? CONTAINER_SAS_DEFAULT_DURATION);
        var blobUriBuilder = new BlobUriBuilder(uri);
        var accountKey = await _storage.GetStorageAccountNameKeyByName(blobUriBuilder.AccountName);
        var sasBuilder = new BlobSasBuilder(
                BlobContainerSasPermissions.Read | BlobContainerSasPermissions.Write | BlobContainerSasPermissions.Delete | BlobContainerSasPermissions.List,
                endTime) {
            BlobContainerName = blobUriBuilder.BlobContainerName,
            StartsOn = startTime
        };

        var sas = sasBuilder.ToSasQueryParameters(new StorageSharedKeyCredential(blobUriBuilder.AccountName, accountKey)).ToString();
        return new UriBuilder(uri) {
            Query = sas
        }.Uri;
    }

    public async Async.Task<Uri> GetContainerSasUrl(Container container, StorageType storageType, BlobContainerSasPermissions permissions, TimeSpan? duration = null) {
        var client = await FindContainer(container, storageType) ?? throw new Exception($"unable to find container: {container.ContainerName} - {storageType}");
        var (startTime, endTime) = SasTimeWindow(duration ?? CONTAINER_SAS_DEFAULT_DURATION);
        var sasBuilder = new BlobSasBuilder(permissions, endTime) {
            StartsOn = startTime,
            BlobContainerName = _config.OneFuzzStoragePrefix + container.ContainerName,
        };

        var sasUrl = client.GenerateSasUri(sasBuilder);
        return sasUrl;
    }

    public async Async.Task<bool> BlobExists(Container container, string name, StorageType storageType) {
        var client = await FindContainer(container, storageType) ?? throw new Exception($"unable to find container: {container.ContainerName} - {storageType}");
        return await client.GetBlobClient(name).ExistsAsync();
    }

    public async Task<Dictionary<string, IDictionary<string, string>>> GetContainers(StorageType corpus) {
        var accounts = _storage.GetAccounts(corpus);
        IEnumerable<IEnumerable<KeyValuePair<string, IDictionary<string, string>>>> data =
         await Async.Task.WhenAll(accounts.Select(async acc => {
             var service = await _storage.GetBlobServiceClientForAccount(acc);
             if (service is null) {
                 throw new InvalidOperationException($"unable to get blob service for account {acc}");
             }

             return await service.GetBlobContainersAsync(BlobContainerTraits.Metadata).Select(container =>
                KeyValuePair.Create(container.Name, container.Properties.Metadata)).ToListAsync();
         }));

        return new(data.SelectMany(x => x));
    }
}
