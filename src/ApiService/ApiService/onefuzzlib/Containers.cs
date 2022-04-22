using System.Threading.Tasks;
using Azure.ResourceManager;
using Azure.Storage.Blobs;
using Azure.Storage;
using Azure;

namespace Microsoft.OneFuzz.Service;

public interface IContainers
{
    public Task<IEnumerable<byte>?> GetBlob(Container container, string name, StorageType storageType);

    public Async.Task<BlobContainerClient?> FindContainer(Container container, StorageType storageType);

    public Uri GetFileSasUrl(Container container, string name, StorageType storageType, bool read = false, bool add = false, bool create = false, bool write = false, bool delete = false, bool delete_previous_version = false, bool tag = false, int days = 30, int hours = 0, int minutes = 0);

    public Async.Task<Guid> GetInstanceId();
}

public class Containers : IContainers
{
    private ILogTracer _log;
    private IStorage _storage;
    private ICreds _creds;
    private ArmClient _armClient;
    public Containers(ILogTracer log, IStorage storage, ICreds creds)
    {
        _log = log;
        _storage = storage;
        _creds = creds;
        _armClient = new ArmClient(credential: _creds.GetIdentity(), defaultSubscriptionId: _creds.GetSubcription());
    }
    public async Task<IEnumerable<byte>?> GetBlob(Container container, string name, StorageType storageType)
    {
        var client = await FindContainer(container, storageType);

        if (client == null)
        {
            return null;
        }

        try
        {
            return (await client.GetBlobClient(name).DownloadContentAsync())
                .Value.Content.ToArray();
        }
        catch (RequestFailedException)
        {
            return null;
        }
    }

    public async Async.Task<BlobContainerClient?> FindContainer(Container container, StorageType storageType)
    {
        // # check secondary accounts first by searching in reverse.
        // #
        // # By implementation, the primary account is specified first, followed by
        // # any secondary accounts.
        // #
        // # Secondary accounts, if they exist, are preferred for containers and have
        // # increased IOP rates, this should be a slight optimization
        return await _storage.GetAccounts(storageType)
            .Reverse()
            .Select(account => GetBlobService(account)?.GetBlobContainerClient(container.ContainerName))
            .ToAsyncEnumerable()
            .WhereAwait(async client => client != null && (await client.ExistsAsync()).Value)
            .FirstOrDefaultAsync();
    }

    private BlobServiceClient? GetBlobService(string accountId)
    {
        _log.Info($"getting blob container (account_id: {accountId}");
        var (accountName, accountKey) = _storage.GetStorageAccountNameAndKey(accountId);
        if (accountName == null)
        {
            _log.Error("Failed to get storage account name");
            return null;
        }
        var storageKeyCredential = new StorageSharedKeyCredential(accountName, accountKey);
        var accountUrl = GetUrl(accountName);
        return new BlobServiceClient(accountUrl, storageKeyCredential);
    }

    private static Uri GetUrl(string accountName)
    {
        return new Uri($"https://{accountName}.blob.core.windows.net/");
    }

    public Uri GetFileSasUrl(Container container, string name, StorageType storageType, bool read = false, bool add = false, bool create = false, bool write = false, bool delete = false, bool delete_previous_version = false, bool tag = false, int days = 30, int hours = 0, int minutes = 0)
    {
        throw new NotImplementedException();
    }

    // Moved From Creds.cs
    public async Async.Task<Guid> GetInstanceId()
    {
        var blob = await GetBlob(new Container("base-config"), "instance_id", StorageType.Config);
        if (blob == null)
        {
            throw new System.Exception("Blob Not Found");
        }
        return System.Guid.Parse(System.Text.Encoding.Default.GetString(blob.ToArray()));
    }
}
