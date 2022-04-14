using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using Azure.ResourceManager;
using Azure.Storage.Blobs;
using Azure.Storage;
using Azure;

namespace Microsoft.OneFuzz.Service;

public interface IContainers {
    public Task<IEnumerable<byte>?> GetBlob(Container container, string name, StorageType storageType);

    public BlobContainerClient? FindContainer(Container container, StorageType storageType);
}

public class Containers : IContainers {
    private LogTracer _loggerTracer;
    private IStorage _storage;
    private ICreds _creds;
    private ArmClient _armClient;
    public Containers(ILogTracerFactory loggerFactory, IStorage storage, ICreds creds)
    {
        _loggerTracer = loggerFactory.MakeLogTracer(Guid.NewGuid());
        _storage = storage;
        _creds = creds;
        _armClient = new ArmClient(credential: _creds.GetIdentity(), defaultSubscriptionId: _creds.GetSubcription());
    }
    public async Task<IEnumerable<byte>?> GetBlob(Container container, string name, StorageType storageType)
    {
        var client = FindContainer(container, storageType);

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

    public BlobContainerClient? FindContainer(Container container, StorageType storageType)
    {
        var accounts = _storage.GetAccounts(_loggerTracer, storageType);

        // # check secondary accounts first by searching in reverse.
        // #
        // # By implementation, the primary account is specified first, followed by
        // # any secondary accounts.
        // #
        // # Secondary accounts, if they exist, are preferred for containers and have
        // # increased IOP rates, this should be a slight optimization

        foreach (var account in accounts.Reverse())
        {
            var client = GetBlobService(account)?.GetBlobContainerClient(container.ContainerName);
            if (client?.Exists().Value ?? false)
            {
                return client;
            }
        }
        return null;
    }

    private BlobServiceClient? GetBlobService(string accountId)
    {
        _loggerTracer.Info($"getting blob container (account_id: {accountId}");
        var (accountName, accountKey) = _storage.GetStorageAccountNameAndKey(accountId);
        if (accountName == null)
        {
            _loggerTracer.Error("Failed to get storage account name");
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
}

