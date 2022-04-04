using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ApiService;
using Microsoft.OneFuzz.Service;

namespace ApiService.onefuzzlib.orm
{

    public interface IStorageProvider
    {
        Task<TableClient> GetTableClient(string table);
        IAsyncEnumerable<T> QueryAsync<T>(string filter) where T : EntityBase;
        Task<bool> Replace<T>(T entity) where T : EntityBase;

    }

    public class StorageProvider : IStorageProvider
    {
        private readonly string _accountId;
        private readonly EntityConverter _entityConverter;

        public StorageProvider(string accountId) {
            _accountId = accountId;
            _entityConverter = new EntityConverter();
        }

        public async Task<TableClient> GetTableClient(string table)
        {
            var (name, key) = GetStorageAccountNameAndKey(_accountId);
            var tableClient = new TableServiceClient(new Uri(_accountId), new TableSharedKeyCredential(name, key));
            await tableClient.CreateTableIfNotExistsAsync(table);
            return tableClient.GetTableClient(table);
        }

        private (string, string) GetStorageAccountNameAndKey(string accounId)
        {
            throw new NotImplementedException();
        }

        public async IAsyncEnumerable<T> QueryAsync<T>(string filter) where T : EntityBase
        {
            var tableClient = await GetTableClient(typeof(T).Name);

            await foreach (var x in tableClient.QueryAsync<TableEntity>(filter).Select(x => _entityConverter.ToRecord<T>(x))) { 
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
    }

}