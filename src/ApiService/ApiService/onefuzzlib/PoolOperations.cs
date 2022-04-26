using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IPoolOperations {
    public Async.Task<Result<Pool, Error>> GetByName(string poolName);

}

public class PoolOperations : StatefulOrm<Pool, PoolState>, IPoolOperations {
    private IConfigOperations _configOperations;
    private ITaskOperations _taskOperations;

    public PoolOperations(IStorage storage, ILogTracer log, IServiceConfig config, IConfigOperations configOperations, ITaskOperations taskOperations)
        : base(storage, log, config) {
        _configOperations = configOperations;
        _taskOperations = taskOperations;
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
}
