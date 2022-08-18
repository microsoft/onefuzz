using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure.Data.Tables;

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

    Async.Task<Pool> Init(Pool pool);
}

public class PoolOperations : StatefulOrm<Pool, PoolState, PoolOperations>, IPoolOperations {

    public PoolOperations(ILogTracer log, IOnefuzzContext context)
        : base(log, context) {

    }

    public async Async.Task<OneFuzzResult<Pool>> GetByName(PoolName poolName) {
        var pools = QueryAsync(Query.PartitionKey(poolName.String));

        var result = await pools.ToListAsync();
        if (result.Count == 0) {
            return OneFuzzResult<Pool>.Error(ErrorCode.INVALID_REQUEST, "unable to find pool");
        }

        if (result.Count != 1) {
            return OneFuzzResult<Pool>.Error(ErrorCode.INVALID_REQUEST, "error identifying pool");
        }

        return OneFuzzResult<Pool>.Ok(result.Single());
    }

    public async Async.Task<OneFuzzResult<Pool>> GetById(Guid poolId) {
        var pools = QueryAsync(Query.RowKey(poolId.ToString()));

        var result = await pools.ToListAsync();
        if (result.Count == 0) {
            return OneFuzzResult<Pool>.Error(ErrorCode.INVALID_REQUEST, "unable to find pool");
        }

        if (result.Count != 1) {
            return OneFuzzResult<Pool>.Error(ErrorCode.INVALID_REQUEST, "error identifying pool");
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
        return QueryAsync(filter: TableClient.CreateQueryFilter($"client_id eq {clientId}"));
    }

    public string GetPoolQueue(Guid poolId)
        => $"pool-{poolId:N}";

    public async Async.Task<List<ScalesetSummary>> GetScalesetSummary(PoolName name)
        => await _context.ScalesetOperations.SearchByPool(name)
            .Select(x => new ScalesetSummary(ScalesetId: x!.ScalesetId, State: x.State))
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

        // scalesets should never leave the `halt` state
        // it is terminal
        if (pool.State == PoolState.Halt) {
            return pool;
        }

        pool = pool with { State = state };
        await Update(pool);
        return pool;
    }

    public async Async.Task<Pool> Init(Pool pool) {
        await _context.Queue.CreateQueue(GetPoolQueue(pool.PoolId), StorageType.Corpus);
        var shrinkQueue = new ShrinkQueue(pool.PoolId, _context.Queue, _logTracer);
        await shrinkQueue.Create();
        await SetState(pool, PoolState.Running);
        return pool;
    }

    public async Async.Task<Pool> Shutdown(Pool pool) {
        var scalesets = _context.ScalesetOperations.SearchByPool(pool.Name);
        var nodes = _context.NodeOperations.SearchByPoolName(pool.Name);

        if (scalesets is null && nodes is null) {
            _logTracer.Info($"pool stopped, deleting {pool.Name}");
            await Delete(pool);
            return pool;
        }

        if (scalesets is not null) {
            await foreach (var scaleset in scalesets) {
                if (scaleset is not null) {
                    await _context.ScalesetOperations.SetShutdown(scaleset, now: true);
                }
            }
        }

        if (nodes is not null) {
            await foreach (var node in nodes) {
                await _context.NodeOperations.SetShutdown(node);
            }
        }

        //TODO: why do we save pool here ? there are no changes to pool record...
        //if it was changed by the caller - caller should perform save operation
        var r = await Update(pool);
        if (!r.IsOk) {
            _logTracer.Error($"Failed to update pool record. pool name: {pool.Name}, pool id: {pool.PoolId}");
        }
        return pool;
    }

    public async Async.Task<Pool> Halt(Pool pool) {
        //halt the pool immediately
        var scalesets = _context.ScalesetOperations.SearchByPool(pool.Name);
        var nodes = _context.NodeOperations.SearchByPoolName(pool.Name);

        if (scalesets is null && nodes is null) {
            var poolQueue = GetPoolQueue(pool.PoolId);
            await _context.Queue.DeleteQueue(poolQueue, StorageType.Corpus);
            var shrinkQueue = new ShrinkQueue(pool.PoolId, _context.Queue, _logTracer);
            await shrinkQueue.Delete();
            _logTracer.Info($"pool stopped, deleting: {pool.Name}");
            var r = await Delete(pool);
            if (!r.IsOk) {
                _logTracer.Error($"Failed to delete pool: {pool.Name} due to {r.ErrorV}");
            }
        }

        if (scalesets is not null) {
            await foreach (var scaleset in scalesets) {
                if (scaleset is not null) {
                    await _context.ScalesetOperations.SetState(scaleset, ScalesetState.Halt);
                }
            }
        }

        if (nodes is not null) {
            await foreach (var node in nodes) {
                await _context.NodeOperations.SetHalt(node);
            }
        }

        return pool;
    }
}
