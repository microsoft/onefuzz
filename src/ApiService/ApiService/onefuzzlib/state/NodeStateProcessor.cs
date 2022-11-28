using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.DurableTask;

namespace Microsoft.OneFuzz.Service;

public record struct NodeKey(
    PoolName PoolName,
    Guid MachineId
);

[DurableTask]
class NodeStateProcessor : TaskOrchestratorBase<JsonElement, bool> {
    protected override async Async.Task<bool> OnRunAsync(
        TaskOrchestrationContext context,
        JsonElement json) {

        _ = await context.CallNodeStateTransitionAsync(
            json.ToString(),
            options: TaskOptions.FromRetryPolicy(
                new RetryPolicy(1000, TimeSpan.FromSeconds(1))));

        return true;
    }
}

[DurableTask]
class NodeStateTransition : TaskActivityBase<string, bool> {
    private readonly INodeOperations _nodeOps;
    private readonly ILogTracer _log;

    public NodeStateTransition(INodeOperations nodeOps, ILogTracer log) {
        _nodeOps = nodeOps;
        _log = log;
    }

    protected override async Task<bool> OnRunAsync(
        TaskActivityContext context,
        string? json) {

        var input = JsonSerializer.Deserialize<NodeKey>(json!);
        var node = await _nodeOps.GetEntityAsync(input.PoolName.ToString(), input.MachineId.ToString());
        if (node is not null) {
            _log.Info($"updating node: {input.PoolName:Tag:PoolName} {input.MachineId:Tag:MachineId} - state: {node.State:Tag:NodeState}");
            node = await _nodeOps.ProcessStateUpdate(node);
            _log.Info($"finished updating node: {input.PoolName:Tag:PoolName} {input.MachineId:Tag:MachineId} - state: {node.State:Tag:NodeState}");
            return true;
        } else {
            _log.Info($"node not found: {input.PoolName:Tag:PoolName} {input.MachineId:Tag:MachineId}");
            return false;
        }
    }
}
