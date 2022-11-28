using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.DurableTask;

namespace Microsoft.OneFuzz.Service;

public record struct PoolKey(
    PoolName PoolName,
    Guid PoolId
);

[DurableTask]
class PoolStateOrchestrator : TaskOrchestratorBase<JsonElement, bool> {
    protected override async Async.Task<bool> OnRunAsync(
        TaskOrchestrationContext context,
        JsonElement json) {

        _ = await context.CallPoolState_UpdateAsync(
            json.ToString(),
            options: TaskOptions.FromRetryPolicy(
                new RetryPolicy(1000, TimeSpan.FromSeconds(1))));

        return true;
    }
}

[DurableTask]
class PoolState_Update : TaskActivityBase<string, bool> {
    private readonly IPoolOperations _poolOps;
    private readonly ILogTracer _log;

    public PoolState_Update(IPoolOperations poolOps, ILogTracer log) {
        _poolOps = poolOps;
        _log = log;
    }

    protected override async Task<bool> OnRunAsync(
        TaskActivityContext context,
        string? json) {
        var input = JsonSerializer.Deserialize<PoolKey>(json!);
        var pool = await _poolOps.GetEntityAsync(input.PoolName.ToString(), input.PoolId.ToString());
        if (pool is not null) {
            _log.Info($"updating pool: {input.PoolId:Tag:PoolId} ({input.PoolName:Tag:PoolName}) - state: {pool.State:Tag:PoolState}");
            _ = await _poolOps.ProcessStateUpdate(pool);
            _log.Info($"finished updating pool: {input.PoolId:Tag:PoolId} ({input.PoolName:Tag:PoolName}) - state: {pool.State:Tag:PoolState}");
            return true;
        } else {
            _log.Info($"pool not found: {input.PoolId:Tag:PoolId} ({input.PoolName:Tag:PoolName})");
            return false;
        }
    }
}
