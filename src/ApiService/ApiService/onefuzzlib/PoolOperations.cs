using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IPoolOperations {
    public Async.Task<Result<Pool, Error>> GetByName(string poolName);
    Task<bool> ScheduleWorkset(Pool pool, WorkSet workSet);
}

public class PoolOperations : StatefulOrm<Pool, PoolState>, IPoolOperations {
    private IConfigOperations _configOperations;
    private readonly IQueue _queue;

    public PoolOperations(IStorage storage, ILogTracer log, IServiceConfig config, IConfigOperations configOperations, IQueue queue)
        : base(storage, log, config) {
        _configOperations = configOperations;
        _queue = queue;
    }

    public async Async.Task<Result<Pool, Error>> GetByName(string poolName) {
        var pools = QueryAsync(filter: $"name eq '{poolName}'");

        if (pools == null) {
            return new Result<Pool, Error>(new Error(ErrorCode.INVALID_REQUEST, new[] { "unable to find pool" }));
        }

        if (await pools.CountAsync() != 1) {
            return new Result<Pool, Error>(new Error(ErrorCode.INVALID_REQUEST, new[] { "error identifying pool" }));
        }

        return new Result<Pool, Error>(await pools.SingleAsync());
    }

    public async Task<bool> ScheduleWorkset(Pool pool, WorkSet workSet) {
        if (pool.State == PoolState.Shutdown || pool.State == PoolState.Halt) {
            return false;
        }

        return await _queue.QueueObject(GetPoolQueue(pool), workSet, StorageType.Corpus);
    }

    private string GetPoolQueue(Pool pool) {
        return $"pool-{pool.PoolId.ToString("N")}";
    }
}
