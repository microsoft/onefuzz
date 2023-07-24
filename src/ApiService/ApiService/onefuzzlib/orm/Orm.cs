using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace ApiService.OneFuzzLib.Orm {
    public interface IOrm<T> where T : EntityBase {
        Task<TableClient> GetTableClient(string table, ResourceIdentifier? accountId = null);
        IAsyncEnumerable<T> QueryAsync(string? filter = null, int? maxPerPage = null);

        Task<T> GetEntityAsync(string partitionKey, string rowKey);
        Task<ResultVoid<(HttpStatusCode Status, string Reason)>> Insert(T entity);
        Task<ResultVoid<(HttpStatusCode Status, string Reason)>> Replace(T entity);
        Task<ResultVoid<(HttpStatusCode Status, string Reason)>> Update(T entity);
        Task<ResultVoid<(HttpStatusCode Status, string Reason)>> Delete(T entity);
        Task<Result<bool, (HttpStatusCode Status, string Reason)>> DeleteIfExists(string partitionKey, string rowKey);

        Task<DeleteAllResult> DeleteAll(IEnumerable<(string?, string?)> keys);

        IAsyncEnumerable<T> SearchAll();
        IAsyncEnumerable<T> SearchByPartitionKeys(IEnumerable<string> partitionKeys);
        IAsyncEnumerable<T> SearchByRowKeys(IEnumerable<string> rowKeys);
        IAsyncEnumerable<T> SearchByTimeRange(DateTimeOffset min, DateTimeOffset max);

        // Allow using tuple to search.
        IAsyncEnumerable<T> SearchByTimeRange((DateTimeOffset min, DateTimeOffset max) range)
            => SearchByTimeRange(range.min, range.max);
    }

    public record DeleteAllResult(int SuccessCount, int FailureCount);

    public abstract class Orm<T> : IOrm<T> where T : EntityBase {
#pragma warning disable CA1051 // permit visible instance fields
        protected readonly EntityConverter _entityConverter;
        protected readonly IOnefuzzContext _context;
        protected readonly ILogger _logTracer;
#pragma warning restore CA1051

        const int MAX_TRANSACTION_SIZE = 100;

        public Orm(ILogger logTracer, IOnefuzzContext context) {
            _context = context;
            _logTracer = logTracer;
            _entityConverter = _context.EntityConverter;
        }

        public async IAsyncEnumerable<T> QueryAsync(string? filter = null, int? maxPerPage = null) {
            var tableClient = await GetTableClient(typeof(T).Name);

            if (filter == "") {
                filter = null;
            }

            await foreach (var x in tableClient.QueryAsync<TableEntity>(filter: filter, maxPerPage: maxPerPage).Select(x => _entityConverter.ToRecord<T>(x))) {
                yield return x;
            }
        }

        /// Inserts the entity into table storage.
        /// If successful, updates the ETag of the passed-in entity.
        public async Task<ResultVoid<(HttpStatusCode Status, string Reason)>> Insert(T entity) {
            try {
                var tableClient = await GetTableClient(typeof(T).Name);
                var tableEntity = await _entityConverter.ToTableEntity(entity);
                var response = await tableClient.AddEntityAsync(tableEntity);


                if (response.IsError) {
                    return Result.Error(((HttpStatusCode)response.Status, response.ReasonPhrase));
                } else {
                    // update ETag on success
                    entity.ETag = response.Headers.ETag;
                    return Result.Ok();
                }
            } catch (RequestFailedException ex) {
                return Result.Error(((HttpStatusCode)ex.Status, ex.Message));
            }
        }

        public async Task<ResultVoid<(HttpStatusCode Status, string Reason)>> Replace(T entity) {
            try {
                var tableClient = await GetTableClient(typeof(T).Name);
                var tableEntity = await _entityConverter.ToTableEntity(entity);
                var response = await tableClient.UpsertEntityAsync(tableEntity, TableUpdateMode.Replace);
                if (response.IsError) {
                    return Result.Error(((HttpStatusCode)response.Status, response.ReasonPhrase));
                } else {
                    // update ETag on success
                    entity.ETag = response.Headers.ETag;
                    return Result.Ok();
                }
            } catch (RequestFailedException ex) {
                return Result.Error(((HttpStatusCode)ex.Status, ex.Message));
            }
        }

        public async Task<ResultVoid<(HttpStatusCode Status, string Reason)>> Update(T entity) {
            if (entity.ETag is null) {
                throw new ArgumentException("ETag must be set when updating an entity", nameof(entity));
            }

            try {
                var tableClient = await GetTableClient(typeof(T).Name);
                var tableEntity = await _entityConverter.ToTableEntity(entity);

                var response = await tableClient.UpdateEntityAsync(tableEntity, entity.ETag.Value);
                if (response.IsError) {
                    return Result.Error(((HttpStatusCode)response.Status, response.ReasonPhrase));
                } else {
                    // update ETag on success
                    entity.ETag = response.Headers.ETag;
                    return Result.Ok();
                }
            } catch (RequestFailedException ex) {
                return Result.Error(((HttpStatusCode)ex.Status, ex.Message));
            }
        }

        public async Task<T> GetEntityAsync(string partitionKey, string rowKey) {
            var tableClient = await GetTableClient(typeof(T).Name);
            var tableEntity = await tableClient.GetEntityAsync<TableEntity>(partitionKey, rowKey);
            return _entityConverter.ToRecord<T>(tableEntity);
        }

        public async Task<TableClient> GetTableClient(string table, ResourceIdentifier? accountId = null) {
            var tableName = _context.ServiceConfiguration.OneFuzzStoragePrefix + table;
            var account = accountId ?? _context.ServiceConfiguration.OneFuzzFuncStorage;
            var tableClient = await _context.Storage.GetTableServiceClientForAccount(account);
            return tableClient.GetTableClient(tableName);
        }

        public async Task<ResultVoid<(HttpStatusCode Status, string Reason)>> Delete(T entity) {
            try {
                var tableClient = await GetTableClient(typeof(T).Name);
                var tableEntity = await _entityConverter.ToTableEntity(entity);
                var response = await tableClient.DeleteEntityAsync(tableEntity.PartitionKey, tableEntity.RowKey);
                if (response.IsError) {
                    return Result.Error(((HttpStatusCode)response.Status, response.ReasonPhrase));
                } else {
                    return Result.Ok();
                }
            } catch (RequestFailedException ex) {
                return Result.Error(((HttpStatusCode)ex.Status, ex.Message));
            }
        }

        public async Task<Result<bool, (HttpStatusCode Status, string Reason)>> DeleteIfExists(string partitionKey, string rowKey) {
            try {
                var tableClient = await GetTableClient(typeof(T).Name);
                var result = await tableClient.DeleteEntityAsync(partitionKey, rowKey);
                return Result.Ok(result.Status >= 200 && result.Status < 300);
            } catch (RequestFailedException ex) {
                return Result.Error(((HttpStatusCode)ex.Status, ex.Message));
            }
        }

        public IAsyncEnumerable<T> SearchAll()
            => QueryAsync(null);

        public IAsyncEnumerable<T> SearchByPartitionKeys(IEnumerable<string> partitionKeys)
            => QueryAsync(Query.PartitionKeys(partitionKeys));

        public IAsyncEnumerable<T> SearchByRowKeys(IEnumerable<string> rowKeys)
            => QueryAsync(Query.RowKeys(rowKeys));

        public IAsyncEnumerable<T> SearchByTimeRange(DateTimeOffset min, DateTimeOffset max) {
            return QueryAsync(Query.TimeRange(min, max));
        }

        public async Task<ResultVoid<(int statusCode, string reason, int? failedTransactionIndex)>> BatchOperation(IAsyncEnumerable<T> entities, TableTransactionActionType actionType) {
            try {
                var tableClient = await GetTableClient(typeof(T).Name);
                var transactions = await entities.SelectAwait(async e => {
                    var tableEntity = await _entityConverter.ToTableEntity(e);
                    return new TableTransactionAction(actionType, tableEntity);
                }).ToListAsync();
                var responses = await tableClient.SubmitTransactionAsync(transactions);
                var wrappingResponse = responses.GetRawResponse();
                if (wrappingResponse.IsError) {
                    return Result.Error((wrappingResponse.Status, wrappingResponse.ReasonPhrase, (int?)null));
                }

                var subTransactionFailures = responses.Value.Where(response => response.IsError);
                if (subTransactionFailures.Any()) {
                    var failedTransaction = subTransactionFailures.First();
                    var failedTransactionIndex = responses.Value.ToList().IndexOf(failedTransaction);
                    return Result.Error((failedTransaction.Status, failedTransaction.ReasonPhrase, (int?)failedTransactionIndex));
                }

                return Result.Ok();
            } catch (RequestFailedException ex) {
                int? failedTransactionIndex = null;
                if (ex is TableTransactionFailedException ttfex) {
                    failedTransactionIndex = ttfex.FailedTransactionActionIndex;
                }

                return Result.Error((ex.Status, ex.Message, failedTransactionIndex));
            }
        }


        public async Task<DeleteAllResult> DeleteAll(IEnumerable<(string?, string?)> keys) {
            var query = Query.Or(
                keys.Select(key =>
                    key switch {
                        (null, null) => throw new ArgumentException("partitionKey and rowKey cannot both be null"),
                        (string partitionKey, null) => Query.PartitionKey(partitionKey),
                        (null, string rowKey) => Query.RowKey(rowKey),
                        (string partitionKey, string rowKey) => Query.And(
                            Query.PartitionKey(partitionKey),
                            Query.RowKey(rowKey)
                        ),
                    }
                )
            );

            var tableClient = await GetTableClient(typeof(T).Name);
            var pages = tableClient.QueryAsync<TableEntity>(query, select: new[] { "PartitionKey, RowKey" });

            var requests = await pages
                .Chunk(MAX_TRANSACTION_SIZE)
                .Select(chunk => {
                    var transactions = chunk.Select(e => new TableTransactionAction(TableTransactionActionType.Delete, e));
                    return tableClient.SubmitTransactionAsync(transactions);
                })
                .ToListAsync();

            var responses = await System.Threading.Tasks.Task.WhenAll(requests);
            var (successes, failures) = responses
                .SelectMany(x => x.Value)
                .Aggregate(
                    (0, 0),
                    ((int Successes, int Failures) acc, Response current) =>
                        current.IsError
                        ? (acc.Successes, acc.Failures + 1)
                        : (acc.Successes + 1, acc.Failures)
            );

            return new DeleteAllResult(successes, failures);
        }
    }


    public interface IStatefulOrm<T, TState> : IOrm<T> where T : StatefulEntityBase<TState> where TState : Enum {
        Async.Task<T> ProcessStateUpdate(T entity);

        Async.Task<T?> ProcessStateUpdates(T entity, int MaxUpdates = 5);
    }


    public abstract class StatefulOrm<T, TState, TSelf> : Orm<T>, IStatefulOrm<T, TState> where T : StatefulEntityBase<TState> where TState : Enum {
        static readonly Func<T, object?>? _partitionKeyGetter;
        static readonly Func<T, object?>? _rowKeyGetter;
        static ConcurrentDictionary<string, Func<T, Async.Task<T>>?> _stateFuncs = new ConcurrentDictionary<string, Func<T, Async.Task<T>>?>();

        delegate Async.Task<T> StateTransition(T entity);


        static StatefulOrm() {

            /// verify that all state transition function have the correct signature:
            var thisType = typeof(TSelf);
            var states = Enum.GetNames(typeof(TState));
            var delegateType = typeof(StateTransition);
            MethodInfo delegateSignature = delegateType.GetMethod("Invoke")!;

            var missing = new List<string>();
            foreach (var state in states) {
                var methodInfo = thisType.GetMethod(state.ToString());
                if (methodInfo == null) {
                    missing.Add(state);
                    continue;
                }

                bool parametersEqual = delegateSignature
                    .GetParameters()
                    .Select(x => x.ParameterType)
                    .SequenceEqual(methodInfo.GetParameters()
                        .Select(x => x.ParameterType));

                if (delegateSignature.ReturnType == methodInfo.ReturnType && parametersEqual) {
                    continue;
                }

                throw new InvalidOperationException($"State transition method '{state}' in '{thisType.Name}' does not have the correct signature. Expected '{delegateSignature}'  actual '{methodInfo}' ");
            };

            if (missing.Any()) {
                throw new InvalidOperationException($"State transitions are missing for '{thisType.Name}': {string.Join(", ", missing)}");
            }

            _partitionKeyGetter = EntityConverter.PartitionKeyGetter<T>();


            _rowKeyGetter = EntityConverter.RowKeyGetter<T>();

        }

        public StatefulOrm(ILogger logTracer, IOnefuzzContext context) : base(logTracer, context) {
        }

        /// <summary>
        /// process a single state update, if the obj
        /// implements a function for that state
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public async Async.Task<T> ProcessStateUpdate(T entity) {
            TState state = entity.State;
            var func = GetType().GetMethod(state.ToString()) switch {
                null => null,
                MethodInfo info => info.CreateDelegate<StateTransition>(this)
            };

            if (func != null) {
                var partitionKey = _partitionKeyGetter?.Invoke(entity);
                var rowKey = _rowKeyGetter?.Invoke(entity);
                _logTracer.LogInformation("processing state update: {Type} - {PartitionKey} {RowKey} - {State}", typeof(T), partitionKey, rowKey, state);
                return await func(entity);
            } else {
                throw new ArgumentException($"State function for state: '{state}' not found on type {typeof(T)}");
            }
        }

        /// <summary>
        /// process through the state machine for an object
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="MaxUpdates"></param>
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
