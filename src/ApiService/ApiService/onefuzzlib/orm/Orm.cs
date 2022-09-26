﻿using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace ApiService.OneFuzzLib.Orm {
    public interface IOrm<T> where T : EntityBase {
        Task<TableClient> GetTableClient(string table, ResourceIdentifier? accountId = null);
        IAsyncEnumerable<T> QueryAsync(string? filter = null);

        Task<T> GetEntityAsync(string partitionKey, string rowKey);
        Task<ResultVoid<(int, string)>> Insert(T entity);
        Task<ResultVoid<(int, string)>> Replace(T entity);
        Task<ResultVoid<(int, string)>> Update(T entity);
        Task<ResultVoid<(int, string)>> Delete(T entity);

        IAsyncEnumerable<T> SearchAll();
        IAsyncEnumerable<T> SearchByPartitionKeys(IEnumerable<string> partitionKeys);
        IAsyncEnumerable<T> SearchByRowKeys(IEnumerable<string> rowKeys);
        IAsyncEnumerable<T> SearchByTimeRange(DateTimeOffset min, DateTimeOffset max);

        // Allow using tuple to search.
        IAsyncEnumerable<T> SearchByTimeRange((DateTimeOffset min, DateTimeOffset max) range)
            => SearchByTimeRange(range.min, range.max);
    }


    public abstract class Orm<T> : IOrm<T> where T : EntityBase {
#pragma warning disable CA1051 // permit visible instance fields
        protected readonly EntityConverter _entityConverter;
        protected readonly IOnefuzzContext _context;
        protected readonly ILogTracer _logTracer;
#pragma warning restore CA1051


        public Orm(ILogTracer logTracer, IOnefuzzContext context) {
            _context = context;
            _logTracer = logTracer;
            _entityConverter = _context.EntityConverter;
        }

        public async IAsyncEnumerable<T> QueryAsync(string? filter = null) {
            var tableClient = await GetTableClient(typeof(T).Name);

            if (filter == "") {
                filter = null;
            }

            await foreach (var x in tableClient.QueryAsync<TableEntity>(filter).Select(x => _entityConverter.ToRecord<T>(x))) {
                yield return x;
            }
        }

        /// Inserts the entity into table storage.
        /// If successful, updates the ETag of the passed-in entity.
        public async Task<ResultVoid<(int, string)>> Insert(T entity) {
            var tableClient = await GetTableClient(typeof(T).Name);
            var tableEntity = _entityConverter.ToTableEntity(entity);
            var response = await tableClient.AddEntityAsync(tableEntity);

            if (response.IsError) {
                return ResultVoid<(int, string)>.Error((response.Status, response.ReasonPhrase));
            } else {
                // update ETag
                entity.ETag = response.Headers.ETag;

                return ResultVoid<(int, string)>.Ok();
            }
        }

        public async Task<ResultVoid<(int, string)>> Replace(T entity) {
            var tableClient = await GetTableClient(typeof(T).Name);
            var tableEntity = _entityConverter.ToTableEntity(entity);
            var response = await tableClient.UpsertEntityAsync(tableEntity, TableUpdateMode.Replace);
            if (response.IsError) {
                return ResultVoid<(int, string)>.Error((response.Status, response.ReasonPhrase));
            } else {
                return ResultVoid<(int, string)>.Ok();
            }
        }

        public async Task<ResultVoid<(int, string)>> Update(T entity) {
            if (entity.ETag is null) {
                throw new ArgumentException("ETag must be set when updating an entity", nameof(entity));
            }

            var tableClient = await GetTableClient(typeof(T).Name);
            var tableEntity = _entityConverter.ToTableEntity(entity);

            var response = await tableClient.UpdateEntityAsync(tableEntity, entity.ETag.Value);
            if (response.IsError) {
                return ResultVoid<(int, string)>.Error((response.Status, response.ReasonPhrase));
            } else {
                return ResultVoid<(int, string)>.Ok();
            }
        }

        public async Task<T> GetEntityAsync(string partitionKey, string rowKey) {
            var tableClient = await GetTableClient(typeof(T).Name);
            var tableEntity = await tableClient.GetEntityAsync<TableEntity>(partitionKey, rowKey);
            return _entityConverter.ToRecord<T>(tableEntity);
        }

        public async Task<TableClient> GetTableClient(string table, ResourceIdentifier? accountId = null) {
            var tableName = _context.ServiceConfiguration.OneFuzzStoragePrefix + table;
            var account = accountId ?? _context.ServiceConfiguration.OneFuzzFuncStorage ?? throw new ArgumentNullException(nameof(accountId));
            var tableClient = await _context.Storage.GetTableServiceClientForAccount(account);
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

        public IAsyncEnumerable<T> SearchByPartitionKeys(IEnumerable<string> partitionKeys)
            => QueryAsync(Query.PartitionKeys(partitionKeys));

        public IAsyncEnumerable<T> SearchByRowKeys(IEnumerable<string> rowKeys)
            => QueryAsync(Query.RowKeys(rowKeys));

        public IAsyncEnumerable<T> SearchByTimeRange(DateTimeOffset min, DateTimeOffset max) {
            return QueryAsync(Query.TimeRange(min, max));
        }
    }


    public interface IStatefulOrm<T, TState> : IOrm<T> where T : StatefulEntityBase<TState> where TState : Enum {
        Async.Task<T?> ProcessStateUpdate(T entity);

        Async.Task<T?> ProcessStateUpdates(T entity, int MaxUpdates = 5);
    }


    public abstract class StatefulOrm<T, TState, TSelf> : Orm<T>, IStatefulOrm<T, TState> where T : StatefulEntityBase<TState> where TState : Enum {
        static Func<T, object?>? _partitionKeyGetter;
        static Func<T, object?>? _rowKeyGetter;
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
                MethodInfo info => info.CreateDelegate<StateTransition>(this)
            };

            if (func != null) {
                var partitionKey = _partitionKeyGetter?.Invoke(entity);
                var rowKey = _rowKeyGetter?.Invoke(entity);
                _logTracer.Info($"processing state update: {typeof(T)} - PartitionKey: {partitionKey} RowKey: {rowKey} - {state}");
                return await func(entity);
            } else {
                _logTracer.Info($"State function for state: '{state}' not found on type {typeof(T)}");
            }
            return null;
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
