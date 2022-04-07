using Azure.Core;
using Azure.Data.Tables;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApiService.OneFuzzLib.Orm
{
    public interface IOrm
    {

    }

    public class Orm : IOrm
    {
        IStorage _storage;
        EntityConverter _entityConverter;

        public Orm(IStorage storage)
        {
            _storage = storage;
            _entityConverter = new EntityConverter();
        }

        public async IAsyncEnumerable<T> QueryAsync<T>(string filter) where T : EntityBase
        {
            var tableClient = await GetTableClient(typeof(T).Name);

            await foreach (var x in tableClient.QueryAsync<TableEntity>(filter).Select(x => _entityConverter.ToRecord<T>(x)))
            {
                yield return x;
            }
        }

        public async Task<bool> Replace<T>(T entity) where T : EntityBase
        {
            var tableClient = await GetTableClient(typeof(T).Name);
            var tableEntity = _entityConverter.ToTableEntity(entity);
            var response = await tableClient.UpsertEntityAsync(tableEntity);
            return !response.IsError;

        }

        public async Task<TableClient> GetTableClient(string table, string? accountId = null)
        {
            var account = accountId ?? EnvironmentVariables.OneFuzz.FuncStorage ?? throw new ArgumentNullException(nameof(accountId));
            var (name, key) = _storage.GetStorageAccountNameAndKey(account);
            var identifier = new ResourceIdentifier(account);
            var tableClient = new TableServiceClient(new Uri($"https://{identifier.Name}.table.core.windows.net"), new TableSharedKeyCredential(name, key));
            await tableClient.CreateTableIfNotExistsAsync(table);
            return tableClient.GetTableClient(table);
        }
    }
}
