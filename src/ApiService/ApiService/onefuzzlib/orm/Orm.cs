using Azure.Data.Tables;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ApiService.OneFuzzLib.Orm
{
    public interface IOrm<T> where T : EntityBase
    {
        Task<TableClient> GetTableClient(string table, string? accountId = null);
        IAsyncEnumerable<T> QueryAsync(string filter);
        Task<bool> Replace(T entity);
    }

    public class Orm<T> : IOrm<T> where T : EntityBase
    {
        IStorage _storage;
        EntityConverter _entityConverter;

        public Orm(IStorage storage)
        {
            _storage = storage;
            _entityConverter = new EntityConverter();
        }

        public async IAsyncEnumerable<T> QueryAsync(string? filter = null)
        {
            var tableClient = await GetTableClient(typeof(T).Name);

            await foreach (var x in tableClient.QueryAsync<TableEntity>(filter).Select(x => _entityConverter.ToRecord<T>(x)))
            {
                yield return x;
            }
        }

        public async Task<bool> Replace(T entity)
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
            var tableClient = new TableServiceClient(new Uri($"https://{name}.table.core.windows.net"), new TableSharedKeyCredential(name, key));
            await tableClient.CreateTableIfNotExistsAsync(table);
            return tableClient.GetTableClient(table);
        }
    }
}
