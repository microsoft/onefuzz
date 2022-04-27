﻿using System.Threading.Tasks;
using Azure;
using Azure.ResourceManager;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace Microsoft.OneFuzz.Service;


public interface IContainers {
    public Task<BinaryData?> GetBlob(Container container, string name, StorageType storageType);

    public Async.Task<BlobContainerClient?> FindContainer(Container container, StorageType storageType);

    public Async.Task<Uri?> GetFileSasUrl(Container container, string name, StorageType storageType, BlobSasPermissions permissions, TimeSpan? duration = null);
    Async.Task saveBlob(Container container, string v1, string v2, StorageType config);
    Task<Guid> GetInstanceId();
    Uri AddContainerSasUrl(Uri uri);
    Async.Task<Uri> GetContainerSasUrl(Container container, StorageType storageType, BlobContainerSasPermissions permissions, TimeSpan? duration = null);
}

public class Containers : IContainers {
    private ILogTracer _log;
    private IStorage _storage;
    private ICreds _creds;
    private ArmClient _armClient;
    public Containers(ILogTracer log, IStorage storage, ICreds creds) {
        _log = log;
        _storage = storage;
        _creds = creds;
        _armClient = creds.ArmClient;
    }
    public async Task<BinaryData?> GetBlob(Container container, string name, StorageType storageType) {
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

    public async Async.Task<BlobContainerClient?> FindContainer(Container container, StorageType storageType) {
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

    private BlobServiceClient? GetBlobService(string accountId) {
        _log.Info($"getting blob container (account_id: {accountId}");
        var (accountName, accountKey) = _storage.GetStorageAccountNameAndKey(accountId);
        if (accountName == null) {
            _log.Error("Failed to get storage account name");
            return null;
        }
        var storageKeyCredential = new StorageSharedKeyCredential(accountName, accountKey);
        var accountUrl = GetUrl(accountName);
        return new BlobServiceClient(accountUrl, storageKeyCredential);
    }

    private static Uri GetUrl(string accountName) {
        return new Uri($"https://{accountName}.blob.core.windows.net/");
    }

    public async Async.Task<Uri?> GetFileSasUrl(Container container, string name, StorageType storageType, BlobSasPermissions permissions, TimeSpan? duration = null) {
        var client = await FindContainer(container, storageType) ?? throw new Exception($"unable to find container: {container.ContainerName} - {storageType}");
        var (accountName, accountKey) = _storage.GetStorageAccountNameAndKey(client.AccountName);

        var (startTime, endTime) = SasTimeWindow(duration ?? TimeSpan.FromDays(30));

        var sasBuilder = new BlobSasBuilder(permissions, endTime) {
            StartsOn = startTime,
            BlobContainerName = container.ContainerName,
            BlobName = name
        };

        var sasUrl = client.GetBlobClient(name).GenerateSasUri(sasBuilder);
        return sasUrl;
    }

    public (DateTimeOffset, DateTimeOffset) SasTimeWindow(TimeSpan timeSpan) {
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

    public async System.Threading.Tasks.Task saveBlob(Container container, string name, string data, StorageType storageType) {
        var client = await FindContainer(container, storageType) ?? throw new Exception($"unable to find container: {container.ContainerName} - {storageType}");

        await client.UploadBlobAsync(name, new BinaryData(data));
    }

    public async Async.Task<Guid> GetInstanceId() {
        var blob = await GetBlob(new Container("base-config"), "instance_id", StorageType.Config);
        if (blob == null) {
            throw new System.Exception("Blob Not Found");
        }
        return System.Guid.Parse(blob.ToString());
    }

    public Uri AddContainerSasUrl(Uri uri) {
        if (uri.Query.Contains("sig")) {
            return uri;
        }

        var accountName = uri.Host.Split('.')[0];
        var (_, accountKey) = _storage.GetStorageAccountNameAndKey(accountName);
        var sasBuilder = new BlobSasBuilder(
                BlobContainerSasPermissions.Read | BlobContainerSasPermissions.Write | BlobContainerSasPermissions.Delete | BlobContainerSasPermissions.List,
                DateTimeOffset.UtcNow + TimeSpan.FromHours(1));

        var sas = sasBuilder.ToSasQueryParameters(new StorageSharedKeyCredential(accountName, accountKey)).ToString();
        return new UriBuilder(uri) {
            Query = sas
        }.Uri;
    }

    public async Async.Task<Uri> GetContainerSasUrl(Container container, StorageType storageType, BlobContainerSasPermissions permissions, TimeSpan? duration = null) {
        var client = await FindContainer(container, storageType) ?? throw new Exception($"unable to find container: {container.ContainerName} - {storageType}");
        var (accountName, accountKey) = _storage.GetStorageAccountNameAndKey(client.AccountName);

        var (startTime, endTime) = SasTimeWindow(duration ?? TimeSpan.FromDays(30));

        var sasBuilder = new BlobSasBuilder(permissions, endTime) {
            StartsOn = startTime,
            BlobContainerName = container.ContainerName,
        };

        var sasUrl = client.GenerateSasUri(sasBuilder);
        return sasUrl;
    }
}

