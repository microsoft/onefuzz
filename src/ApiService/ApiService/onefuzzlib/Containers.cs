using System;
using Azure.Storage;
using Azure.Storage.Blobs;
using Container = System.String;

namespace Microsoft.OneFuzz.Service;

public interface IContainers
{
    public Async.Task<byte[]> GetBlob(Container container, string name, StorageType storageType);
}

public class Containers : IContainers
{

    private ILogTracer _log;
    private IStorage _storage;
    
    public Containers(ILogTracer log, IStorage storage)
    {
        _log = log;
        _storage = storage;
    }

    private Uri GetUrl(string accountName)
    {
        return new Uri($"https://{accountName}.blob.core.windows.net/");
    }
        

    private BlobServiceClient GetBlobService(string accountId)
    {
        _log.Info($"getting blob container (account_id: {accountId}");
        var (accountName, accountKey) = _storage.GetStorageAccountNameAndKey(accountId);
        if (accountName == null || accountKey == null)
        {
            throw new System.Exception($"Could not find storage account with accountId: {accountId}");
        }
        var accountUrl = GetUrl(accountName);
        var service = new BlobServiceClient(serviceUri:accountUrl, credential: new StorageSharedKeyCredential(accountName, accountKey));
        
        return service;
    }
    private BlobContainerClient? FindContainer(Container container, StorageType storageType)
    {
        var accounts = _storage.GetAccounts(storageType);

        // check secondary accounts first by searching in reverse.
        //
        // By implementation, the primary account is specified first, followed by
        // any secondary accounts.
        //
        // Secondary accounts, if they exist, are preferred for containers and have
        // increased IOP rates, this should be a slight optimization
        accounts.Reverse();
        foreach (var account in accounts)
        {
            var client = GetBlobService(account).GetBlobContainerClient(container);
            if (client.Exists())
            {
                return client;
            }
        }
        return null;
    }
    public async Async.Task<byte[]> GetBlob(Container container, string name, StorageType storageType)
    {
        var client = FindContainer(container, storageType);
        if (client == null)
        {
            return null;
        }

        try 
        {   
            // let! r = client.GetBlobClient(name).DownloadContentAsync()
            // return Some(r.Value.Content.ToArray())
            var content = await client.GetBlobClient(name).DownloadContentAsync();
            return content.Value.Content.ToArray();
        } catch (Exception)
        {
            return null;
        }
    } 
}