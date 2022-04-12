using Azure.Data.Tables;
using System;
using System.Linq;
using System.Threading.Tasks;
using Azure.Core;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager;
using Azure.Identity;

namespace Microsoft.OneFuzz.Service.OneFuzzLib.Orm;


public interface IStorageProvider
{
    Task<TableClient> GetTableClient(string table);
    //IAsyncEnumerable<T> QueryAsync<T>(string filter) where T : EntityBase;
    //Task<bool> Replace<T>(T entity) where T : EntityBase;

}

public class StorageProvider : IStorageProvider
{
    private readonly string _accountId;
    private readonly EntityConverter _entityConverter;
    private readonly ArmClient _armClient;

    public StorageProvider(string accountId)
    {
        _accountId = accountId;
        _entityConverter = new EntityConverter();
        _armClient = new ArmClient(new DefaultAzureCredential());
    }

    public async Task<TableClient> GetTableClient(string table)
    {
        var (name, key) = GetStorageAccountNameAndKey(_accountId);
        var identifier = new ResourceIdentifier(_accountId);
        var tableClient = new TableServiceClient(new Uri($"https://{identifier.Name}.table.core.windows.net"), new TableSharedKeyCredential(name, key));
        await tableClient.CreateTableIfNotExistsAsync(table);
        return tableClient.GetTableClient(table);
    }


    public (string?, string?) GetStorageAccountNameAndKey(string accountId)
    {
        var resourceId = new ResourceIdentifier(accountId);
        var storageAccount = _armClient.GetStorageAccountResource(resourceId);
        var key = storageAccount.GetKeys().Value.Keys.FirstOrDefault();
        return (resourceId.Name, key?.Value);
    }


}