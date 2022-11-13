using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.DurableTask;

namespace Microsoft.OneFuzz.Service;

public record struct PoolKey(
    string PoolName,
    string PoolId
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
        _log.Info($"inner got json: {json}");
        var input = JsonSerializer.Deserialize<PoolKey>(json!);
        _log.Info($"getting pool: {input.PoolId:Tag:PoolId} ({input.PoolName:Tag:PoolName})");
        var poolResult = await _poolOps.GetById(Guid.Parse(input.PoolId));
        if (poolResult.IsOk) {
            var pool = poolResult.OkV;
            // var pool = await _poolOps.GetEntityAsync(input.PoolName, input.PoolId);
            _log.Info($"updating pool: {input.PoolId:Tag:PoolId} ({input.PoolName:Tag:PoolName}) - state: {pool.State:Tag:PoolState}");
            _ = await _poolOps.ProcessStateUpdate(pool);
            _log.Info($"finished updating pool: {input.PoolId:Tag:PoolId} ({input.PoolName:Tag:PoolName}) - state: {pool.State:Tag:PoolState}");
            return true;
        }

        return false;
    }
}
