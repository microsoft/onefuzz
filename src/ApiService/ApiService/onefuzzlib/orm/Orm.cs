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
        IAsyncEnumerable<T> QueryAsync(string? filter = null);
        Task<ResultOk<(int, string)>> Replace(T entity);

        Task<T> GetEntityAsync(string partitionKey, string rowKey);
        Task<ResultOk<(int, string)>> Insert(T entity);
        Task<ResultOk<(int, string)>> Delete(T entity);
        Async.Task ProcessStateUpdate(T entity);

        Async.Task ProcessStateUpdates(T entity, int MaxUpdates = 5);
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

        public async Task<ResultOk<(int, string)>> Insert(T entity)
        {
            var tableClient = await GetTableClient(typeof(T).Name);
            var tableEntity = _entityConverter.ToTableEntity(entity);
            var response = await tableClient.AddEntityAsync(tableEntity);

            if (response.IsError)
            {
                return ResultOk<(int, string)>.Error((response.Status, response.ReasonPhrase));
            }
            else
            {
                return ResultOk<(int, string)>.Ok();
            }
        }

        public async Task<ResultOk<(int, string)>> Replace(T entity)
        {
            var tableClient = await GetTableClient(typeof(T).Name);
            var tableEntity = _entityConverter.ToTableEntity(entity);
            var response = await tableClient.UpsertEntityAsync(tableEntity);
            if (response.IsError)
            {
                return ResultOk<(int, string)>.Error((response.Status, response.ReasonPhrase));
            }
            else
            {
                return ResultOk<(int, string)>.Ok();
            }
        }

        public async Task<ResultOk<(int, string)>> Update(T entity)
        {
            var tableClient = await GetTableClient(typeof(T).Name);
            var tableEntity = _entityConverter.ToTableEntity(entity);

            if (entity.ETag is null)
            {
                return ResultOk<(int, string)>.Error((0, "ETag must be set when updating an entity"));
            }
            else
            {
                var response = await tableClient.UpdateEntityAsync(tableEntity, entity.ETag.Value);
                if (response.IsError)
                {
                    return ResultOk<(int, string)>.Error((response.Status, response.ReasonPhrase));
                }
                else
                {
                    return ResultOk<(int, string)>.Ok();
                }
            }
        }

        public async Task<T> GetEntityAsync(string partitionKey, string rowKey)
        {
            var tableClient = await GetTableClient(typeof(T).Name);
            var tableEntity = await tableClient.GetEntityAsync<TableEntity>(partitionKey, rowKey);
            return _entityConverter.ToRecord<T>(tableEntity);
        }

        public async Task<TableClient> GetTableClient(string table, string? accountId = null)
        {
            var account = accountId ?? EnvironmentVariables.OneFuzz.FuncStorage ?? throw new ArgumentNullException(nameof(accountId));
            var (name, key) = _storage.GetStorageAccountNameAndKey(account);
            var tableClient = new TableServiceClient(new Uri($"https://{name}.table.core.windows.net"), new TableSharedKeyCredential(name, key));
            await tableClient.CreateTableIfNotExistsAsync(table);
            return tableClient.GetTableClient(table);
        }

        public async Task<ResultOk<(int, string)>> Delete(T entity)
        {
            var tableClient = await GetTableClient(typeof(T).Name);
            var tableEntity = _entityConverter.ToTableEntity(entity);
            var response = await tableClient.DeleteEntityAsync(tableEntity.PartitionKey, tableEntity.RowKey);
            if (response.IsError)
            {
                return ResultOk<(int, string)>.Error((response.Status, response.ReasonPhrase));
            }
            else
            {
                return ResultOk<(int, string)>.Ok();
            }
        }

        public System.Threading.Tasks.Task ProcessStateUpdate(T entity)
        {
            throw new NotImplementedException();
        }

        public System.Threading.Tasks.Task ProcessStateUpdates(T entity, int MaxUpdates = 5)
        {
            throw new NotImplementedException();
        }
    }
}
