using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Microsoft.DurableTask;

namespace Microsoft.OneFuzz.Service;

public record struct NodeKey(
    PoolName PoolName,
    Guid MachineId
);

[DurableTask]
class NodeStateProcessor : TaskOrchestratorBase<JsonElement, bool> {
    static readonly RetryPolicy _persistentRetryPolicy
        = new(
            maxNumberOfAttempts: 1000,
            firstRetryInterval: TimeSpan.FromSeconds(1),
            backoffCoefficient: 1.2);

    protected override async Async.Task<bool> OnRunAsync(
        TaskOrchestrationContext context,
        JsonElement json) {

        _ = await context.CallNodeStateTransitionAsync(
            json.ToString(),
            options: TaskOptions.FromRetryPolicy(_persistentRetryPolicy));

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
        Node node;
        try {
            node = await _nodeOps.GetEntityAsync(input.PoolName.ToString(), input.MachineId.ToString());
        } catch (RequestFailedException ex) when (ex.Status == 404) {
            _log.Info($"node not found: {input.PoolName:Tag:PoolName} {input.MachineId:Tag:MachineId}");
            return false; // nothing to be done
        }

        _log.Info($"updating node: {input.PoolName:Tag:PoolName} {input.MachineId:Tag:MachineId} - state: {node.State:Tag:NodeState}");
        node = await _nodeOps.ProcessStateUpdate(node);
        _log.Info($"finished updating node: {input.PoolName:Tag:PoolName} {input.MachineId:Tag:MachineId} - state: {node.State:Tag:NodeState}");
        return true;
    }
}
