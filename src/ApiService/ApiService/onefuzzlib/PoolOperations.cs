using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
namespace Microsoft.OneFuzz.Service;
using Microsoft.Extensions.Logging;
public interface IPoolOperations : IStatefulOrm<Pool, PoolState> {
    Async.Task<OneFuzzResult<Pool>> GetByName(PoolName poolName);
    Async.Task<OneFuzzResult<Pool>> GetById(Guid poolId);
    Task<bool> ScheduleWorkset(Pool pool, WorkSet workSet);
    IAsyncEnumerable<Pool> GetByObjectId(Guid objectId);
    string GetPoolQueue(Guid poolId);
    Async.Task<List<ScalesetSummary>> GetScalesetSummary(PoolName name);
    Async.Task<List<WorkSetSummary>> GetWorkQueue(Guid poolId, PoolState state);
    IAsyncEnumerable<Pool> SearchStates(IEnumerable<PoolState> states);
    Async.Task<Pool> SetShutdown(Pool pool, bool Now);

    Async.Task<Pool> Create(PoolName name, Os os, Architecture architecture, bool managed, Guid? objectId = null);
    new Async.Task Delete(Pool pool);

    // state transitions:
    Async.Task<Pool> Init(Pool pool);
    Async.Task<Pool> Running(Pool pool);
    Async.Task<Pool> Shutdown(Pool pool);
    Async.Task<Pool> Halt(Pool pool);

    public static string PoolQueueNamePrefix => "pool-";
}

public class PoolOperations : StatefulOrm<Pool, PoolState, PoolOperations>, IPoolOperations {

    public PoolOperations(ILogger<PoolOperations> log, IOnefuzzContext context)
        : base(log, context) {

    }

    public async Async.Task<Pool> Create(PoolName name, Os os, Architecture architecture, bool managed, Guid? objectId = null) {
        var newPool = new Service.Pool(
        PoolId: Guid.NewGuid(),
        State: PoolState.Init,
        Name: name,
        Os: os,
        Managed: managed,
        Arch: architecture,
        ObjectId: objectId);

        var r = await Insert(newPool);
        if (!r.IsOk) {
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("Failed to save new pool. {PoolName} - {PoolId}", newPool.Name, newPool.PoolId);
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

    public IAsyncEnumerable<Pool> GetByObjectId(Guid objectId) {
        return QueryAsync(filter: $"object_id eq '{objectId}'");
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

        _logTracer.AddTags(new Dictionary<string, string>(){
            { "PoolName", pool.Name.ToString() },
            { "PoolId", pool.PoolId.ToString()},
            { "From", pool.State.ToString()},
            { "To", state.ToString()}
        });
        _logTracer.LogEvent("SetState Pool");
        // scalesets should never leave the `halt` state
        // it is terminal
        if (pool.State == PoolState.Halt) {
            return pool;
        }

        pool = pool with { State = state };
        var r = await Replace(pool);
        if (!r.IsOk) {
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("failed to replace pool when setting state");
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
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("Failed to delete pool: {PoolName}", pool.Name);
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
            _logTracer.LogInformation("pool stopped, deleting {PoolName}", pool.Name);
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
            _logTracer.LogError("Failed to update pool record. {PoolName} - {PoolId}", pool.Name, pool.PoolId);
        }

        return pool;
    }

    public async Async.Task<Pool> Halt(Pool pool) {
        //halt the pool immediately
        var scalesets = await _context.ScalesetOperations.SearchByPool(pool.Name).ToListAsync();
        var nodes = await _context.NodeOperations.SearchByPoolName(pool.Name).ToListAsync();

        if (!scalesets.Any() && !nodes.Any()) {
            _logTracer.LogInformation("pool stopped, deleting: {PoolName}", pool.Name);
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
