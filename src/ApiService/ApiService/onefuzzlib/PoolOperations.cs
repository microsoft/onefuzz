using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Microsoft.Azure.Cosmos;

namespace Microsoft.OneFuzz.Service;

public interface IPoolOperations : IStatefulOrm<Pool, PoolState> {
    Async.Task<OneFuzzResult<Pool>> GetByName(PoolName poolName);
    Async.Task<OneFuzzResult<Pool>> GetById(Guid poolId);
    Task<bool> ScheduleWorkset(Pool pool, WorkSet workSet);
    IAsyncEnumerable<Pool> GetByClientId(Guid clientId);
    string GetPoolQueue(Guid poolId);
    Async.Task<List<ScalesetSummary>> GetScalesetSummary(PoolName name);
    Async.Task<List<WorkSetSummary>> GetWorkQueue(Guid poolId, PoolState state);
    IAsyncEnumerable<Pool> SearchStates(IEnumerable<PoolState> states);
    Async.Task<Pool> SetShutdown(Pool pool, bool Now);

    Async.Task<Pool> Create(PoolName name, Os os, Architecture architecture, bool managed, Guid? clientId = null);
    new Async.Task Delete(Pool pool);

    // state transitions:
    Async.Task<Pool> Init(Pool pool);
    Async.Task<Pool> Running(Pool pool);
    Async.Task<Pool> Shutdown(Pool pool);
    Async.Task<Pool> Halt(Pool pool);

    public static string PoolQueueNamePrefix => "pool-";
}

/*

public class CosmosPoolOperations : IPoolOperations {
    private readonly Azure.Cosmos.Container _container;
    private readonly ILogTracer _logTracer;
    private readonly OnefuzzContext _context;

    public CosmosPoolOperations(Azure.Cosmos.Container container, ILogTracer logTracer, OnefuzzContext context) {
        _container = container;
        _logTracer = logTracer;
        _context = context;
    }

    public async Async.Task<Pool> Create(PoolName name, Os os, Architecture architecture, bool managed, Guid? clientId = null) {
        var newPool = new Pool(
            PoolId: Guid.NewGuid(),
            State: PoolState.Init,
            Name: name,
            Os: os,
            Managed: managed,
            Arch: architecture,
            ClientId: clientId);

        try {
            _ = await _container.CreateItemAsync(newPool);
            await _context.Events.SendEvent(new EventPoolCreated(PoolName: newPool.Name, Os: newPool.Os, Arch: newPool.Arch, Managed: newPool.Managed));
            return newPool;
        } catch (CosmosException ex) {
            _logTracer.WithHttpStatus(ex).Error($"Failed to save new pool. {newPool.Name:Tag:PoolName} - {newPool.PoolId:Tag:PoolId}");
            throw;
        }
    }

    public async Async.Task<OneFuzzResult<Pool>> GetByName(PoolName poolName) {
        var q = new QueryDefinition("select * from ROOT p where p.name = @poolName")
            .WithParameter("@poolName", poolName);

        var result = await _container.GetItemQueryAsyncEnumerable<Pool>(q).FirstOrDefaultAsync();
        if (result is not null) {
            return OneFuzzResult.Ok(result);
        }

        return OneFuzzResult<Pool>.Error(ErrorCode.INVALID_REQUEST, $"unable to find pool with name {poolName.String}");
    }

    public async Async.Task<OneFuzzResult<Pool>> GetById(Guid poolId) {
        var q = new QueryDefinition("select * from ROOT p where p.poolId = @poolId")
            .WithParameter("@poolId", poolId);

        var result = await _container.GetItemQueryAsyncEnumerable<Pool>(q).FirstOrDefaultAsync();
        if (result is not null) {
            return OneFuzzResult.Ok(result);
        }

        return OneFuzzResult<Pool>.Error(ErrorCode.INVALID_REQUEST, $"unable to find pool with id {poolId}");
    }

    public async Task<bool> ScheduleWorkset(Pool pool, WorkSet workSet) {
        if (pool.State == PoolState.Shutdown || pool.State == PoolState.Halt) {
            return false;
        }

        return await _context.Queue.QueueObject(GetPoolQueue(pool.PoolId), workSet, StorageType.Corpus);
    }

    public IAsyncEnumerable<Pool> GetByClientId(Guid clientId) {
        var q = new QueryDefinition("select * from ROOT p where p.clientId = @clientId")
            .WithParameter("@clientId", clientId);
        return _container.GetItemQueryAsyncEnumerable<Pool>(q);
    }

    public string GetPoolQueue(Guid poolId)
        => $"{IPoolOperations.PoolQueueNamePrefix}{poolId:N}";

    public async Async.Task<List<ScalesetSummary>> GetScalesetSummary(PoolName name)
        => await _context.ScalesetOperations.SearchByPool(name)
            .Select(x => new ScalesetSummary(ScalesetId: x.ScalesetId, State: x.State))
            .ToListAsync();

    public async Async.Task<List<WorkSetSummary>> GetWorkQueue(Guid poolId, PoolState state) {
        var result = new List<WorkSetSummary>();

        // Only populate the work queue summaries if the pool is initialized. We
        // can then be sure that the queue is available in the operations below.
        if (state == PoolState.Init) {
            return result;
        }

        var workSets = await PeekWorkQueue(poolId);
        foreach (var workSet in workSets) {
            if (!workSet.WorkUnits.Any()) {
                continue;
            }

            var workUnits = workSet.WorkUnits
                .Select(x => new WorkUnitSummary(
                    JobId: x.JobId,
                    TaskId: x.TaskId,
                    TaskType: x.TaskType))
                .ToList();

            result.Add(new WorkSetSummary(workUnits));
        }

        return result;
    }

    private Async.Task<IList<WorkSet>> PeekWorkQueue(Guid poolId)
        => _context.Queue.PeekQueue<WorkSet>(GetPoolQueue(poolId), StorageType.Corpus);

    public IAsyncEnumerable<Pool> SearchStates(IEnumerable<PoolState> states) {
        var q = new QueryDefinition("select * from ROOT p where p.state IN @states")
            .WithParameter("@states", states);

        return _container.GetItemQueryAsyncEnumerable<Pool>(q);
    }

    public Async.Task<Pool> SetShutdown(Pool pool, bool Now)
        => SetState(pool, Now ? PoolState.Halt : PoolState.Shutdown);

    public async Async.Task<Pool> SetState(Pool pool, PoolState state) {
        if (pool.State == state) {
            return pool;
        }

        _logTracer.WithTag("PoolName", pool.Name.ToString()).Event($"SetState Pool {pool.PoolId:Tag:PoolId} {pool.State:Tag:From} - {state:Tag:To}");
        // scalesets should never leave the `halt` state
        // it is terminal
        if (pool.State == PoolState.Halt) {
            return pool;
        }

        var result = await _container.PatchItemAsync<Pool>(
            pool.PoolId.ToString(),
            new PartitionKey(pool.Name.ToString()),
            new[] {
                PatchOperation.Replace("/state", state)
            });

        return result.Resource;
    }

    public async Async.Task<Pool> Init(Pool pool) {
        await _context.Queue.CreateQueue(GetPoolQueue(pool.PoolId), StorageType.Corpus);
        var shrinkQueue = new ShrinkQueue(pool.PoolId, _context.Queue, _logTracer);
        await shrinkQueue.Create();
        return await SetState(pool, PoolState.Running);
    }

    public async Async.Task Delete(Pool pool) {
        _ = await _container.DeleteItemAsync<Pool>(
            pool.PoolId.ToString(),
            new PartitionKey(pool.Name.ToString()));

        var poolQueue = GetPoolQueue(pool.PoolId);
        await _context.Queue.DeleteQueue(poolQueue, StorageType.Corpus);

        var shrinkQueue = new ShrinkQueue(pool.PoolId, _context.Queue, _logTracer);
        await shrinkQueue.Delete();

        await _context.Events.SendEvent(new EventPoolDeleted(PoolName: pool.Name));
    }

    public async Async.Task<Pool> Shutdown(Pool pool) {
        var scalesets = await _context.ScalesetOperations.SearchByPool(pool.Name).ToListAsync();
        var nodes = await _context.NodeOperations.SearchByPoolName(pool.Name).ToListAsync();

        if (!scalesets.Any() && !nodes.Any()) {
            _logTracer.Info($"pool stopped, deleting {pool.Name:Tag:PoolName}");
            await Delete(pool);
            return pool;
        }

        foreach (var scaleset in scalesets) {
            _ = await _context.ScalesetOperations.SetShutdown(scaleset, now: true);
        }

        foreach (var node in nodes) {
            // ignoring updated result - nodes not returned
            _ = await _context.NodeOperations.SetShutdown(node);
        }

        return pool;
    }

    public async Async.Task<Pool> Halt(Pool pool) {
        //halt the pool immediately
        var scalesets = await _context.ScalesetOperations.SearchByPool(pool.Name).ToListAsync();
        var nodes = await _context.NodeOperations.SearchByPoolName(pool.Name).ToListAsync();

        if (!scalesets.Any() && !nodes.Any()) {
            _logTracer.Info($"pool stopped, deleting: {pool.Name:Tag:PoolName}");
            await Delete(pool);
            return pool;
        }

        foreach (var scaleset in scalesets) {
            if (scaleset is not null) {
                _ = await _context.ScalesetOperations.SetState(scaleset, ScalesetState.Halt);
            }
        }

        foreach (var node in nodes) {
            // updated value ignored: 'nodes' is not returned
            _ = await _context.NodeOperations.SetHalt(node);
        }

        return pool;
    }

    public Task<Pool> Running(Pool pool) {
        // nothing to do
        return Async.Task.FromResult(pool);
    }

    // copied in, below

    delegate Async.Task<Pool> StateTransition(Pool entity);
    public async Async.Task<Pool> ProcessStateUpdate(Pool entity) {
        var state = entity.State;
        var func = GetType().GetMethod(state.ToString()) switch {
            null => null,
            MethodInfo info => info.CreateDelegate<StateTransition>(this)
        };

        if (func != null) {
            return await func(entity);
        } else {
            throw new ArgumentException($"State function for state: '{state}' not found on type {typeof(Pool)}");
        }
    }

    public async Async.Task<Pool?> ProcessStateUpdates(Pool entity, int MaxUpdates = 5) {
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
*/

static class CosmosExtensions {
    public static IAsyncEnumerable<T> GetItemQueryAsyncEnumerable<T>(this Azure.Cosmos.Container c, QueryDefinition q)
        => new QueryAsyncEnumerable<T>(c, q);
}

// helper to adapt Cosmos results to IAsyncEnumerable; use 
// … container.GetItemQueryAsyncEnumerable<T>(query);
sealed class QueryAsyncEnumerable<T> : IAsyncEnumerable<T> {
    private readonly Azure.Cosmos.Container _container;
    private readonly QueryDefinition _query;

    public QueryAsyncEnumerable(Azure.Cosmos.Container container, QueryDefinition query) {
        _container = container;
        _query = query;
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new Enumerator(_container.GetItemQueryIterator<T>(_query));

    sealed class Enumerator : IAsyncEnumerator<T> {
        private readonly FeedIterator<T> _iterator;
        private IEnumerator<T>? _currentPage;

        public Enumerator(FeedIterator<T> iterator) {
            _iterator = iterator;
        }

        public T Current { get; private set; } = default!;

        public ValueTask DisposeAsync() {
            _currentPage?.Dispose();
            _iterator.Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync() {
            if (_currentPage?.MoveNext() == true) {
                Current = _currentPage.Current;
                return ValueTask.FromResult(true);
            }

            return FetchNextPage();
        }

        private async ValueTask<bool> FetchNextPage() {
            if (_currentPage is not null) {
                _currentPage.Dispose();
                _currentPage = null;
            }

            if (_iterator.HasMoreResults) {
                _currentPage = (await _iterator.ReadNextAsync()).GetEnumerator();
                if (_currentPage.MoveNext()) {
                    Current = _currentPage.Current;
                    return true;
                }
            }

            return false;
        }
    }
}

public class PoolOperations : StatefulOrm<Pool, PoolState, PoolOperations>, IPoolOperations {

    public PoolOperations(ILogTracer log, IOnefuzzContext context)
        : base(log, context) {

    }

    public async Async.Task<Pool> Create(PoolName name, Os os, Architecture architecture, bool managed, Guid? clientId = null) {
        var newPool = new Service.Pool(
        PoolId: Guid.NewGuid(),
        State: PoolState.Init,
        Name: name,
        Os: os,
        Managed: managed,
        Arch: architecture,
        ClientId: clientId);

        var r = await Insert(newPool);
        if (!r.IsOk) {
            _logTracer.WithHttpStatus(r.ErrorV).Error($"Failed to save new pool. {newPool.Name:Tag:PoolName} - {newPool.PoolId:Tag:PoolId}");
        }
        await _context.Events.SendEvent(new EventPoolCreated(PoolName: newPool.Name, Os: newPool.Os, Arch: newPool.Arch, Managed: newPool.Managed));
        return newPool;
    }
    public async Async.Task<OneFuzzResult<Pool>> GetByName(PoolName poolName) {
        var pools = QueryAsync(Query.PartitionKey(poolName.String));

        var result = await pools.ToListAsync();
        if (result.Count == 0) {
            return OneFuzzResult<Pool>.Error(ErrorCode.INVALID_REQUEST, $"unable to find pool with name {poolName.String}");
        }

        if (result.Count != 1) {
            return OneFuzzResult<Pool>.Error(ErrorCode.INVALID_REQUEST, $"error identifying pool with name {poolName.String}");
        }

        return OneFuzzResult<Pool>.Ok(result.Single());
    }

    public async Async.Task<OneFuzzResult<Pool>> GetById(Guid poolId) {
        var pools = QueryAsync(Query.RowKey(poolId.ToString()));

        var result = await pools.ToListAsync();
        if (result.Count == 0) {
            return OneFuzzResult<Pool>.Error(ErrorCode.INVALID_REQUEST, $"unable to find pool with id {poolId}");
        }

        if (result.Count != 1) {
            return OneFuzzResult<Pool>.Error(ErrorCode.INVALID_REQUEST, $"error identifying pool with id {poolId}");
        }

        return OneFuzzResult<Pool>.Ok(result.Single());
    }

    public async Task<bool> ScheduleWorkset(Pool pool, WorkSet workSet) {
        if (pool.State == PoolState.Shutdown || pool.State == PoolState.Halt) {
            return false;
        }

        return await _context.Queue.QueueObject(GetPoolQueue(pool.PoolId), workSet, StorageType.Corpus);
    }

    public IAsyncEnumerable<Pool> GetByClientId(Guid clientId) {
        return QueryAsync(filter: $"client_id eq '{clientId}'");
    }

    public string GetPoolQueue(Guid poolId)
        => $"{IPoolOperations.PoolQueueNamePrefix}{poolId:N}";

    public async Async.Task<List<ScalesetSummary>> GetScalesetSummary(PoolName name)
        => await _context.ScalesetOperations.SearchByPool(name)
            .Select(x => new ScalesetSummary(ScalesetId: x.ScalesetId, State: x.State))
            .ToListAsync();

    public async Async.Task<List<WorkSetSummary>> GetWorkQueue(Guid poolId, PoolState state) {
        var result = new List<WorkSetSummary>();

        // Only populate the work queue summaries if the pool is initialized. We
        // can then be sure that the queue is available in the operations below.
        if (state == PoolState.Init) {
            return result;
        }

        var workSets = await PeekWorkQueue(poolId);
        foreach (var workSet in workSets) {
            if (!workSet.WorkUnits.Any()) {
                continue;
            }

            var workUnits = workSet.WorkUnits
                .Select(x => new WorkUnitSummary(
                    JobId: x.JobId,
                    TaskId: x.TaskId,
                    TaskType: x.TaskType))
                .ToList();

            result.Add(new WorkSetSummary(workUnits));
        }

        return result;
    }

    private Async.Task<IList<WorkSet>> PeekWorkQueue(Guid poolId)
        => _context.Queue.PeekQueue<WorkSet>(GetPoolQueue(poolId), StorageType.Corpus);

    public IAsyncEnumerable<Pool> SearchStates(IEnumerable<PoolState> states)
        => QueryAsync(Query.EqualAnyEnum("state", states));

    public Async.Task<Pool> SetShutdown(Pool pool, bool Now)
        => SetState(pool, Now ? PoolState.Halt : PoolState.Shutdown);

    public async Async.Task<Pool> SetState(Pool pool, PoolState state) {
        if (pool.State == state) {
            return pool;
        }

        _logTracer.WithTag("PoolName", pool.Name.ToString()).Event($"SetState Pool {pool.PoolId:Tag:PoolId} {pool.State:Tag:From} - {state:Tag:To}");
        // scalesets should never leave the `halt` state
        // it is terminal
        if (pool.State == PoolState.Halt) {
            return pool;
        }

        pool = pool with { State = state };
        var r = await Replace(pool);
        if (!r.IsOk) {
            _logTracer.WithHttpStatus(r.ErrorV).Error($"failed to replace pool {pool.PoolId:Tag:PoolId} when setting state");
        }
        return pool;
    }

    public async Async.Task<Pool> Init(Pool pool) {
        await _context.Queue.CreateQueue(GetPoolQueue(pool.PoolId), StorageType.Corpus);
        var shrinkQueue = new ShrinkQueue(pool.PoolId, _context.Queue, _logTracer);
        await shrinkQueue.Create();
        _ = await SetState(pool, PoolState.Running);
        return pool;
    }

    new public async Async.Task Delete(Pool pool) {
        var r = await base.Delete(pool);
        if (!r.IsOk) {
            _logTracer.WithHttpStatus(r.ErrorV).Error($"Failed to delete pool: {pool.Name:Tag:PoolName}");
        }
        var poolQueue = GetPoolQueue(pool.PoolId);
        await _context.Queue.DeleteQueue(poolQueue, StorageType.Corpus);

        var shrinkQueue = new ShrinkQueue(pool.PoolId, _context.Queue, _logTracer);
        await shrinkQueue.Delete();

        await _context.Events.SendEvent(new EventPoolDeleted(PoolName: pool.Name));
    }

    public async Async.Task<Pool> Shutdown(Pool pool) {
        var scalesets = await _context.ScalesetOperations.SearchByPool(pool.Name).ToListAsync();
        var nodes = await _context.NodeOperations.SearchByPoolName(pool.Name).ToListAsync();

        if (!scalesets.Any() && !nodes.Any()) {
            _logTracer.Info($"pool stopped, deleting {pool.Name:Tag:PoolName}");
            await Delete(pool);
            return pool;
        }

        foreach (var scaleset in scalesets) {
            _ = await _context.ScalesetOperations.SetShutdown(scaleset, now: true);
        }

        foreach (var node in nodes) {
            // ignoring updated result - nodes not returned
            _ = await _context.NodeOperations.SetShutdown(node);
        }

        //TODO: why do we save pool here ? there are no changes to pool record...
        //if it was changed by the caller - caller should perform save operation
        var r = await Update(pool);
        if (!r.IsOk) {
            _logTracer.Error($"Failed to update pool record. {pool.Name:Tag:PoolName} - {pool.PoolId:Tag:PoolId}");
        }

        return pool;
    }

    public async Async.Task<Pool> Halt(Pool pool) {
        //halt the pool immediately
        var scalesets = await _context.ScalesetOperations.SearchByPool(pool.Name).ToListAsync();
        var nodes = await _context.NodeOperations.SearchByPoolName(pool.Name).ToListAsync();

        if (!scalesets.Any() && !nodes.Any()) {
            _logTracer.Info($"pool stopped, deleting: {pool.Name:Tag:PoolName}");
            await Delete(pool);
            return pool;
        }

        foreach (var scaleset in scalesets) {
            if (scaleset is not null) {
                _ = await _context.ScalesetOperations.SetState(scaleset, ScalesetState.Halt);
            }
        }

        foreach (var node in nodes) {
            // updated value ignored: 'nodes' is not returned
            _ = await _context.NodeOperations.SetHalt(node);
        }

        return pool;
    }

    public Task<Pool> Running(Pool pool) {
        // nothing to do
        return Async.Task.FromResult(pool);
    }
}

public static class StatusCodeExtensions {
    public static bool RepresentsSuccess(this HttpStatusCode code)
        => ((int)code) is >= 200 and < 300;
}
