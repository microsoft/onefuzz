using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;

namespace Microsoft.OneFuzz.Service.Functions;

public class TimerWorkers {
    ILogTracer _log;
    IScalesetOperations _scaleSetOps;
    IPoolOperations _poolOps;
    INodeOperations _nodeOps;

    public TimerWorkers(ILogTracer log, IOnefuzzContext context) {
        _log = log;
        _scaleSetOps = context.ScalesetOperations;
        _poolOps = context.PoolOperations;
        _nodeOps = context.NodeOperations;
    }

    private async Async.Task<Service.Scaleset> ProcessScalesets(Service.Scaleset scaleset) {
        _log.Verbose($"checking scaleset for updates: {scaleset.ScalesetId:Tag:ScalesetId}");

        scaleset = await _scaleSetOps.UpdateConfigs(scaleset);
        var r = await _scaleSetOps.SyncAutoscaleSettings(scaleset);
        if (!r.IsOk) {
            _log.Error($"failed to sync auto scale settings {scaleset.ScalesetId:Tag:ScalesetId} due to {r.ErrorV:Tag:Error}");
        }

        // if the scaleset is touched during cleanup, don't continue to process it
        var (touched, ss) = await _scaleSetOps.CleanupNodes(scaleset);
        if (touched) {
            _log.Verbose($"scaleset needed cleanup: {scaleset.ScalesetId:Tag:ScalesetId}");
            return ss;
        }

        scaleset = ss;
        scaleset = await _scaleSetOps.SyncScalesetSize(scaleset);
        return await _scaleSetOps.ProcessStateUpdate(scaleset);
    }

    private static bool ShouldStartStateMachineOrchestration(OrchestrationMetadata? meta)
        => meta is null
        || meta.RuntimeStatus == OrchestrationRuntimeStatus.Completed
        || meta.RuntimeStatus == OrchestrationRuntimeStatus.Failed
        || meta.RuntimeStatus == OrchestrationRuntimeStatus.Terminated;


    [Function("TimerWorkers")]
    public async Async.Task Run(
        [TimerTrigger("00:01:30")] TimerInfo t,
        [DurableClient] DurableClientContext durableClient) {
        // NOTE: Update pools first, such that scalesets impacted by pool updates
        // (such as shutdown or resize) happen during this iteration `timer_worker`
        // rather than the following iteration.

        var pools = _poolOps.SearchStates(states: PoolStateHelper.NeedsWork);
        await foreach (var pool in pools) {
            var instanceId = $"PoolStateOrchestrator-{pool.Name}-{pool.PoolId}";
            try {
                var existingOrchestration = await durableClient.Client.GetInstanceMetadataAsync(instanceId, getInputsAndOutputs: false);
                if (ShouldStartStateMachineOrchestration(existingOrchestration)) {
                    _ = await durableClient.Client.ScheduleNewPoolStateOrchestratorInstanceAsync(
                        instanceId,
                        JsonSerializer.SerializeToElement(new PoolKey(pool.Name, pool.PoolId)));
                }
            } catch (Exception ex) {
                _log.Exception(ex, $"Error triggering pool-state processing for ${instanceId}");
            }
        }

        // NOTE: Nodes, and Scalesets should be processed in a consistent order such
        // during 'pool scale down' operations. This means that pools that are
        // scaling down will more likely remove from the same scalesets over time.
        // By more likely removing from the same scalesets, we are more likely to
        // get to empty scalesets, which can safely be deleted.

        await _nodeOps.MarkOutdatedNodes();
        await _nodeOps.CleanupBusyNodesWithoutWork();

        var nodes = _nodeOps.SearchStates(states: NodeStateHelper.NeedsWorkStates);
        await foreach (var node in nodes) {
            var instanceId = $"NodeStateOrchestrator-{node.PoolName}-{node.MachineId}";
            try {
                var existingOrchestration = await durableClient.Client.GetInstanceMetadataAsync(instanceId, getInputsAndOutputs: false);
                if (ShouldStartStateMachineOrchestration(existingOrchestration)) {
                    _ = await durableClient.Client.ScheduleNewPoolStateOrchestratorInstanceAsync(
                        instanceId,
                        JsonSerializer.SerializeToElement(new NodeKey(node.PoolName, node.MachineId)));
                }
            } catch (Exception ex) {
                _log.Exception(ex, $"Error triggering node-state processing for ${instanceId}");
            }
        }

        var scalesets = _scaleSetOps.SearchAll();
        await foreach (var scaleset in scalesets) {
            try {
                _log.Info($"updating scaleset: {scaleset.ScalesetId:Tag:ScalesetId} - state: {scaleset.State:Tag:ScalesetState}");
                var newScaleset = await ProcessScalesets(scaleset);
                _log.Info($"completed updating scaleset: {scaleset.ScalesetId:Tag:ScalesetId} - now in state {newScaleset.State:Tag:ScalesetState}");
            } catch (Exception ex) {
                _log.Exception(ex, $"failed to process scaleset");
            }
        }
    }
}
