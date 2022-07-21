using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure.Data.Tables;

namespace Microsoft.OneFuzz.Service;

public interface IPoolOperations : IOrm<Pool> {
    public Async.Task<OneFuzzResult<Pool>> GetByName(PoolName poolName);
    public Async.Task<OneFuzzResult<Pool>> GetById(Guid poolId);
    Task<bool> ScheduleWorkset(Pool pool, WorkSet workSet);
    IAsyncEnumerable<Pool> GetByClientId(Guid clientId);
    string GetPoolQueue(Pool pool);
    Async.Task PopulateScalesetSummary(Pool pool);
    Async.Task PopulateWorkQueue(Pool pool);
    IAsyncEnumerable<Pool> SearchStates(IEnumerable<PoolState> state);
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

        return await _context.Queue.QueueObject(GetPoolQueue(pool), workSet, StorageType.Corpus);
    }

    public IAsyncEnumerable<Pool> GetByClientId(Guid clientId) {
        return QueryAsync(filter: TableClient.CreateQueryFilter($"client_id eq {clientId}"));
    }

    public string GetPoolQueue(Pool pool)
        => $"pool-{pool.PoolId:N}";

    public Async.Task PopulateScalesetSummary(Pool pool) {
        throw new NotImplementedException();
    }

    public Async.Task PopulateWorkQueue(Pool pool) {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<Pool> SearchStates(IEnumerable<PoolState> state) {
        throw new NotImplementedException();
    }
}
