using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IPoolOperations {
    public Async.Task<OneFuzzResult<Pool>> GetByName(PoolName poolName);
    Task<bool> ScheduleWorkset(Pool pool, WorkSet workSet);
    IAsyncEnumerable<Pool> GetByClientId(Guid clientId);
}

public class PoolOperations : StatefulOrm<Pool, PoolState>, IPoolOperations {

    public PoolOperations(ILogTracer log, IOnefuzzContext context)
        : base(log, context) {

    }

    public async Async.Task<OneFuzzResult<Pool>> GetByName(PoolName poolName) {
        var pools = QueryAsync(filter: $"PartitionKey eq '{poolName.String}'");

        if (pools == null || await pools.CountAsync() == 0) {
            return OneFuzzResult<Pool>.Error(ErrorCode.INVALID_REQUEST, "unable to find pool");
        }

        if (await pools.CountAsync() != 1) {
            return OneFuzzResult<Pool>.Error(ErrorCode.INVALID_REQUEST, "error identifying pool");
        }

        return OneFuzzResult<Pool>.Ok(await pools.SingleAsync());
    }

    public async Task<bool> ScheduleWorkset(Pool pool, WorkSet workSet) {
        if (pool.State == PoolState.Shutdown || pool.State == PoolState.Halt) {
            return false;
        }

        return await _context.Queue.QueueObject(GetPoolQueue(pool), workSet, StorageType.Corpus);
    }

    public IAsyncEnumerable<Pool> GetByClientId(Guid clientId) {
        return QueryAsync(filter: $"client_id eq '{clientId.ToString()}'");
    }

    private string GetPoolQueue(Pool pool) {
        return $"pool-{pool.PoolId.ToString("N")}";
    }
}
