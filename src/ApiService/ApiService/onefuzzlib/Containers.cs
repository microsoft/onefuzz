﻿using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;


public interface IContainers {
    public Async.Task<(BinaryData? data, IDictionary<string, string>? tags)> GetBlob(Container container, string name, StorageType storageType);

    public Async.Task<Uri?> CreateContainer(Container container, StorageType storageType, IDictionary<string, string>? metadata);

    public Async.Task<BlobContainerClient?> GetOrCreateContainerClient(Container container, StorageType storageType, IDictionary<string, string>? metadata);

    public Async.Task<BlobContainerClient?> FindContainer(Container container, StorageType storageType);

    public Async.Task<Uri?> GetFileSasUrl(Container container, string name, StorageType storageType, BlobSasPermissions permissions, TimeSpan? duration = null);
    public Async.Task SaveBlob(Container container, string name, string data, StorageType storageType, DateOnly? expiresOn = null);
    public Async.Task<Guid> GetInstanceId();

    public Async.Task<Uri?> GetFileUrl(Container container, string name, StorageType storageType);

    public Async.Task<Uri> GetContainerSasUrl(Container container, StorageType storageType, BlobContainerSasPermissions permissions, TimeSpan? duration = null);

    public Async.Task<bool> BlobExists(Container container, string name, StorageType storageType);

    public Async.Task<Uri> AddContainerSasUrl(Uri uri, TimeSpan? duration = null);
    public Async.Task<Dictionary<Container, IDictionary<string, string>>> GetContainers(StorageType corpus);

    public string AuthDownloadUrl(Container container, string filename);
    public Async.Task<OneFuzzResultVoid> DownloadAsZip(Container container, StorageType storageType, Stream stream, string? prefix = null);

    public Async.Task DeleteAllExpiredBlobs();
}

public class Containers : Orm<ContainerInformation>, IContainers {
    private readonly ILogger _log;
    private readonly IStorage _storage;
    private readonly IServiceConfig _config;
    private readonly IMemoryCache _cache;

    static readonly TimeSpan CONTAINER_SAS_DEFAULT_DURATION = TimeSpan.FromDays(30);

    public Containers(
        ILogger<Containers> log,
        IStorage storage,
        IServiceConfig config,
        IOnefuzzContext context,
        IMemoryCache cache) : base(log, context) {

        _log = log;
        _storage = storage;
        _config = config;
        _cache = cache;

        _getInstanceId = new Lazy<Async.Task<Guid>>(async () => {
            var (data, tags) = await GetBlob(WellKnownContainers.BaseConfig, "instance_id", StorageType.Config);
            if (data == null) {
                throw new Exception("Blob Not Found");
            }

            return Guid.Parse(data.ToString());
        }, LazyThreadSafetyMode.PublicationOnly);
    }

    public async Async.Task<Uri?> GetFileUrl(Container container, string name, StorageType storageType) {
        var client = await FindContainer(container, storageType);
        if (client is null)
            return null;

        return client.GetBlobClient(name).Uri;
    }

    public async Async.Task<(BinaryData? data, IDictionary<string, string>? tags)> GetBlob(Container container, string name, StorageType storageType) {
        var client = await FindContainer(container, storageType);
        if (client == null) {
            return (null, null);
        }

        try {
            var blobClient = client.GetBlobClient(name);
            var tags = await blobClient.GetTagsAsync();
            return ((await blobClient.DownloadContentAsync()).Value.Content, tags.Value.Tags);
        } catch (RequestFailedException) {
            return (null, null);
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
        var containerName = _config.OneFuzzStoragePrefix + container;
        var cc = client.GetBlobContainerClient(containerName);
        try {
            var r = await cc.CreateAsync(metadata: metadata);
            if (r.GetRawResponse().IsError) {
                _log.LogError("failed to create blob {ContainerName} due to {Error}", containerName, r.GetRawResponse().ReasonPhrase);
            }
        } catch (RequestFailedException ex) when (ex.ErrorCode == "ContainerAlreadyExists") {
            // note: resource exists error happens during creation if the container
            // is being deleted
            _log.LogError(ex, "unable to create container. {Account} {Container} {Metadata}", account, container, metadata);
            return null;
        }

        return cc;
    }

    private async Task<ContainerInformation?> LoadContainerInformation(Container container, StorageType storageType) {
        var result = await QueryAsync(Query.SingleEntity(storageType.ToString(), container.ToString())).FirstOrDefaultAsync();
        if (result is not null) {
            return result;
        }

        // we don't have metadata about this account yet, find it:
        var containerName = _config.OneFuzzStoragePrefix + container;
        // Check secondary accounts first by searching in reverse.
        // 
        // By implementation, the primary account is specified first, followed by
        // any secondary accounts.
        // 
        // Secondary accounts, if they exist, are preferred for containers and have
        // increased IOP rates, this should be a slight optimization
        foreach (var account in _storage.GetAccounts(storageType).Reverse()) {
            var accountClient = await _storage.GetBlobServiceClientForAccount(account);
            var containerClient = accountClient.GetBlobContainerClient(containerName);
            if (await containerClient.ExistsAsync()) {
                // insert it into the table so we find it next time:
                var containerInfo = new ContainerInformation(storageType, container, account.ToString());
                _ = await Replace(containerInfo);
                return containerInfo;
            }
        }

        return null;
    }

    private sealed record ContainerKey(StorageType storageType, Container container);
    public async Async.Task<BlobContainerClient?> FindContainer(Container container, StorageType storageType) {
        var containerInfo = await _cache.GetOrCreateAsync(
            new ContainerKey(storageType, container),
            async entry => {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
                return await LoadContainerInformation(container, storageType);
            });

        if (containerInfo is null) {
            return null;
        }

        return await _storage.GetBlobContainerClientForResource(new ResourceIdentifier(containerInfo.ResourceId));
    }

    public async Async.Task<Uri?> GetFileSasUrl(Container container, string name, StorageType storageType, BlobSasPermissions permissions, TimeSpan? duration = null) {
        var client = await FindContainer(container, storageType);
        if (client is null) {
            return null;
        }

        var blobClient = client.GetBlobClient(name);
        var timeWindow = SasTimeWindow(duration ?? TimeSpan.FromDays(30));
        return _storage.GenerateBlobSasUri(permissions, blobClient, timeWindow);
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

    public async Async.Task SaveBlob(Container container, string name, string data, StorageType storageType, DateOnly? expiresOn = null) {
        switch (expiresOn) {
            case DateOnly expiryDate:
                var tags = new Dictionary<string, string>();
                var expiryDateTag = RetentionPolicyUtils.CreateExpiryDateTag(expiryDate);
                tags.Add(expiryDateTag.Key, expiryDateTag.Value);

                await SaveBlobInternal(container, name, data, storageType, new BlobUploadOptions {
                    Tags = tags,
                });
                break;
            default:
                await SaveBlobInternal(container, name, data, storageType);
                break;
        }
    }

    private async Async.Task SaveBlobInternal(Container container, string name, string data, StorageType storageType, BlobUploadOptions? blobUploadOptions = null) {
        var client = await FindContainer(container, storageType) ?? throw new Exception($"unable to find container: {container} - {storageType}");
        var blobSave = blobUploadOptions switch {
            null => await client.GetBlobClient(name).UploadAsync(new BinaryData(data), overwrite: true),
            BlobUploadOptions buo => await client.GetBlobClient(name).UploadAsync(new BinaryData(data), buo)
        };
        var r = blobSave.GetRawResponse();
        if (r.IsError) {
            throw new Exception($"failed to save blob {name} due to {r.ReasonPhrase}");
        }
    }

    public virtual Async.Task<Guid> GetInstanceId() => _getInstanceId.Value;
    private readonly Lazy<Async.Task<Guid>> _getInstanceId;

    public Uri GetContainerSasUrlService(
        BlobContainerClient client,
        BlobContainerSasPermissions permissions,
        TimeSpan? timeSpan = null) {
        var timeWindow = SasTimeWindow(timeSpan ?? TimeSpan.FromDays(30.0));
        return _storage.GenerateBlobContainerSasUri(permissions, client, timeWindow);
    }

    public async Async.Task<Uri> AddContainerSasUrl(Uri uri, TimeSpan? duration = null) {
        if (uri.Query.Contains("sig")) {
            return uri;
        }

        var blobUriBuilder = new BlobUriBuilder(uri);
        var serviceClient = await _storage.GetBlobServiceClientForAccountName(blobUriBuilder.AccountName);
        var containerClient = serviceClient.GetBlobContainerClient(blobUriBuilder.BlobContainerName);

        var permissions = BlobContainerSasPermissions.Read | BlobContainerSasPermissions.Write | BlobContainerSasPermissions.Delete | BlobContainerSasPermissions.List;

        var timeWindow = SasTimeWindow(duration ?? CONTAINER_SAS_DEFAULT_DURATION);

        return _storage.GenerateBlobContainerSasUri(permissions, containerClient, timeWindow);
    }

    public async Task<Uri> GetContainerSasUrl(Container container, StorageType storageType, BlobContainerSasPermissions permissions, TimeSpan? duration = null) {
        var client = await FindContainer(container, storageType) ?? throw new Exception($"unable to find container: {container} - {storageType}");
        var timeWindow = SasTimeWindow(duration ?? CONTAINER_SAS_DEFAULT_DURATION);
        return _storage.GenerateBlobContainerSasUri(permissions, client, timeWindow);
    }

    public async Async.Task<bool> BlobExists(Container container, string name, StorageType storageType) {
        var client = await FindContainer(container, storageType) ?? throw new Exception($"unable to find container: {container} - {storageType}");
        return await client.GetBlobClient(name).ExistsAsync();
    }

    public async Task<Dictionary<Container, IDictionary<string, string>>> GetContainers(StorageType corpus) {
        var result = new Dictionary<Container, IDictionary<string, string>>();

        // same container name can exist in multiple accounts; here the last one wins
        foreach (var account in _storage.GetAccounts(corpus)) {
            var service = await _storage.GetBlobServiceClientForAccount(account);
            await foreach (var container in service.GetBlobContainersAsync(BlobContainerTraits.Metadata)) {
                result[Container.Parse(container.Name)] = container.Properties.Metadata;
            }
        }

        return result;
    }

    public string AuthDownloadUrl(Container container, string filename) {
        var instance = _config.OneFuzzInstance;

        var queryString = System.Web.HttpUtility.ParseQueryString(string.Empty);
        queryString.Add("container", container.String);
        queryString.Add("filename", filename);

        return $"{instance}/api/download?{queryString}";
    }

    public async Async.Task<OneFuzzResultVoid> DownloadAsZip(Container container, StorageType storageType, Stream stream, string? prefix = null) {
        var client = await FindContainer(container, storageType) ?? throw new Exception($"unable to find container: {container} - {storageType}");
        var blobs = client.GetBlobs(prefix: prefix);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, true);
        await foreach (var b in blobs.ToAsyncEnumerable()) {
            var entry = archive.CreateEntry(b.Name);
            await using var entryStream = entry.Open();
            var blobClient = client.GetBlockBlobClient(b.Name);
            var downloadResult = await blobClient.DownloadToAsync(entryStream);
            if (downloadResult.IsError) {
                return OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_DOWNLOAD_FILE, $"Error while downloading blob {b.Name}");
            }
        }
        return OneFuzzResultVoid.Ok;
    }

    public async Async.Task DeleteAllExpiredBlobs() {
        var storageTypes = new List<StorageType> { StorageType.Corpus, StorageType.Config };
        var allStorageAccounts = storageTypes.Select(_context.Storage.GetAccounts)
            .SelectMany(x => x);

        await Async.Task.WhenAll(
            allStorageAccounts.Select(async storageAccount => await DeleteExpiredBlobsForAccount(storageAccount))
        );
    }

    private async Async.Task DeleteExpiredBlobsForAccount(ResourceIdentifier storageAccount) {
        var client = await _context.Storage.GetBlobServiceClientForAccount(storageAccount);
        var dryRunEnabled = await _context.FeatureManagerSnapshot.IsEnabledAsync(FeatureFlagConstants.EnableDryRunBlobRetention);

        await foreach (var blob in client.FindBlobsByTagsAsync(RetentionPolicyUtils.CreateExpiredBlobTagFilter())) {
            using var _ = _log.BeginScope("DeletingBlob");
            _log.AddTags(new (string, string)[] {
                ("BlobName", blob.BlobName),
                ("BlobContainer", blob.BlobContainerName)
            });

            if (dryRunEnabled) {
                _log.LogInformation($"Dry run flag enabled, skipping deletion");
                continue;
            }

            try {
                var blobClient = client.GetBlobContainerClient(blob.BlobContainerName);
                var response = await blobClient.DeleteBlobIfExistsAsync(blob.BlobName);
                if (response != null && response.Value) {
                    _log.LogMetric("DeletedExpiredBlob", 1);
                } else {
                    _log.LogMetric("BlobNotDeleted", 1);
                }
            } catch (RequestFailedException ex) {
                // It's ok if we failed to delete the blob, it'll get picked up on the next run
                // But we should still log the exception so we can investigate persistent failures 
                _log.LogWarning(ex.Message);
                _log.LogMetric("FailedDeletingBlob", 1);
            }
        }
    }
}
