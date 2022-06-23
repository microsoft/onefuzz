using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace ApiService.OneFuzzLib.Orm {
    public interface IOrm<T> where T : EntityBase {
        Task<TableClient> GetTableClient(string table, string? accountId = null);
        IAsyncEnumerable<T> QueryAsync(string? filter = null);
        Task<ResultVoid<(int, string)>> Replace(T entity);

        Task<T> GetEntityAsync(string partitionKey, string rowKey);
        Task<ResultVoid<(int, string)>> Insert(T entity);
        Task<ResultVoid<(int, string)>> Delete(T entity);

        IAsyncEnumerable<T> SearchAll();
        IAsyncEnumerable<T> SearchByPartitionKey(string partitionKey);
        IAsyncEnumerable<T> SearchByRowKey(string rowKey);
        IAsyncEnumerable<T> SearchByTimeRange(DateTimeOffset min, DateTimeOffset max);

        // Allow using tuple to search.
        IAsyncEnumerable<T> SearchByTimeRange((DateTimeOffset min, DateTimeOffset max) range)
            => SearchByTimeRange(range.min, range.max);
    }


    public class Orm<T> : IOrm<T> where T : EntityBase {
        protected readonly EntityConverter _entityConverter;
        protected readonly IOnefuzzContext _context;
        protected readonly ILogTracer _logTracer;


        public Orm(ILogTracer logTracer, IOnefuzzContext context) {
            _context = context;
            _logTracer = logTracer;
            _entityConverter = new EntityConverter();
        }

        public async IAsyncEnumerable<T> QueryAsync(string? filter = null) {
            var tableClient = await GetTableClient(typeof(T).Name);

            await foreach (var x in tableClient.QueryAsync<TableEntity>(filter).Select(x => _entityConverter.ToRecord<T>(x))) {
                yield return x;
            }
        }

        public async Task<ResultVoid<(int, string)>> Insert(T entity) {
            var tableClient = await GetTableClient(typeof(T).Name);
            var tableEntity = _entityConverter.ToTableEntity(entity);
            var response = await tableClient.AddEntityAsync(tableEntity);

            if (response.IsError) {
                return ResultVoid<(int, string)>.Error((response.Status, response.ReasonPhrase));
            } else {
                return ResultVoid<(int, string)>.Ok();
            }
        }

        public async Task<ResultVoid<(int, string)>> Replace(T entity) {
            var tableClient = await GetTableClient(typeof(T).Name);
            var tableEntity = _entityConverter.ToTableEntity(entity);
            var response = await tableClient.UpsertEntityAsync(tableEntity);
            if (response.IsError) {
                return ResultVoid<(int, string)>.Error((response.Status, response.ReasonPhrase));
            } else {
                return ResultVoid<(int, string)>.Ok();
            }
        }

        public async Task<ResultVoid<(int, string)>> Update(T entity) {
            var tableClient = await GetTableClient(typeof(T).Name);
            var tableEntity = _entityConverter.ToTableEntity(entity);

            if (entity.ETag is null) {
                return ResultVoid<(int, string)>.Error((0, "ETag must be set when updating an entity"));
            } else {
                var response = await tableClient.UpdateEntityAsync(tableEntity, entity.ETag.Value);
                if (response.IsError) {
                    return ResultVoid<(int, string)>.Error((response.Status, response.ReasonPhrase));
                } else {
                    return ResultVoid<(int, string)>.Ok();
                }
            }
        }

        public async Task<T> GetEntityAsync(string partitionKey, string rowKey) {
            var tableClient = await GetTableClient(typeof(T).Name);
            var tableEntity = await tableClient.GetEntityAsync<TableEntity>(partitionKey, rowKey);
            return _entityConverter.ToRecord<T>(tableEntity);
        }

        public async Task<TableClient> GetTableClient(string table, string? accountId = null) {
            // TODO: do this less often, instead of once per request:
            var tableName = _context.ServiceConfiguration.OneFuzzStoragePrefix + table;

            var account = accountId ?? _context.ServiceConfiguration.OneFuzzFuncStorage ?? throw new ArgumentNullException(nameof(accountId));
            var (name, key) = await _context.Storage.GetStorageAccountNameAndKey(account);
            var endpoint = _context.Storage.GetTableEndpoint(name);
            var tableClient = new TableServiceClient(endpoint, new TableSharedKeyCredential(name, key));
            await tableClient.CreateTableIfNotExistsAsync(tableName);
            return tableClient.GetTableClient(tableName);
        }

        public async Task<ResultVoid<(int, string)>> Delete(T entity) {
            var tableClient = await GetTableClient(typeof(T).Name);
            var tableEntity = _entityConverter.ToTableEntity(entity);
            var response = await tableClient.DeleteEntityAsync(tableEntity.PartitionKey, tableEntity.RowKey);
            if (response.IsError) {
                return ResultVoid<(int, string)>.Error((response.Status, response.ReasonPhrase));
            } else {
                return ResultVoid<(int, string)>.Ok();
            }
        }

        public IAsyncEnumerable<T> SearchAll()
            => QueryAsync(null);

        public IAsyncEnumerable<T> SearchByPartitionKey(string partitionKey)
            => QueryAsync(Query.PartitionKey(partitionKey));

        public IAsyncEnumerable<T> SearchByRowKey(string rowKey)
            => QueryAsync(Query.RowKey(rowKey));

        public IAsyncEnumerable<T> SearchByTimeRange(DateTimeOffset min, DateTimeOffset max) {
            return QueryAsync(Query.TimeRange(min, max));
        }
    }


    public interface IStatefulOrm<T, TState> : IOrm<T> where T : StatefulEntityBase<TState> where TState : Enum {
        Async.Task<T?> ProcessStateUpdate(T entity);

        Async.Task<T?> ProcessStateUpdates(T entity, int MaxUpdates = 5);
    }


    public class StatefulOrm<T, TState> : Orm<T>, IStatefulOrm<T, TState> where T : StatefulEntityBase<TState> where TState : Enum {
        static Lazy<Func<object>>? _partitionKeyGetter;
        static Lazy<Func<object>>? _rowKeyGetter;
        static ConcurrentDictionary<string, Func<T, Async.Task<T>>?> _stateFuncs = new ConcurrentDictionary<string, Func<T, Async.Task<T>>?>();


        static StatefulOrm() {
            _partitionKeyGetter =
                typeof(T).GetProperties().FirstOrDefault(p => p.GetCustomAttributes(true).OfType<PartitionKeyAttribute>().Any())?.GetMethod switch {
                    null => null,
                    MethodInfo info => new Lazy<Func<object>>(() => (Func<object>)Delegate.CreateDelegate(typeof(Func<object>), info), true)
                };

            _rowKeyGetter =
                typeof(T).GetProperties().FirstOrDefault(p => p.GetCustomAttributes(true).OfType<RowKeyAttribute>().Any())?.GetMethod switch {
                    null => null,
                    MethodInfo info => new Lazy<Func<object>>(() => (Func<object>)Delegate.CreateDelegate(typeof(Func<object>), info), true)
                };
        }

        public StatefulOrm(ILogTracer logTracer, IOnefuzzContext context) : base(logTracer, context) {
        }

        /// <summary>
        /// process a single state update, if the obj
        /// implements a function for that state
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public async Async.Task<T?> ProcessStateUpdate(T entity) {
            TState state = entity.State;
            var func = GetType().GetMethod(state.ToString()) switch {
                null => null,
                MethodInfo info => (Func<T, Async.Task<T>>)Delegate.CreateDelegate(typeof(Func<T, Async.Task<T>>), firstArgument: this, method: info)
            };

            if (func != null) {
                _logTracer.Info($"processing state update: {typeof(T)} - PartitionKey {_partitionKeyGetter?.Value()} {_rowKeyGetter?.Value()} - %s");
                return await func(entity);
            }
            return null;
        }

        /// <summary>
        /// process through the state machine for an object
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="MaxUpdates"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Async.Task<T?> ProcessStateUpdates(T entity, int MaxUpdates = 5) {
            for (int i = 0; i < MaxUpdates; i++) {
                var state = entity.State;
                var newEntity = await ProcessStateUpdate(entity);

                if (newEntity == null)
                    return null;

                if (newEntity.State.Equals(state)) {
                    return newEntity;
                }
            }

            return null;
        }
    }

}
