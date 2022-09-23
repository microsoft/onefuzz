using Microsoft.Azure.Functions.Worker;

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

    private async Async.Task ProcessScalesets(Service.Scaleset scaleset) {
        _log.Verbose($"checking scaleset for updates: {scaleset.ScalesetId}");

        scaleset = await _scaleSetOps.UpdateConfigs(scaleset);
        var r = await _scaleSetOps.SyncAutoscaleSettings(scaleset);
        if (!r.IsOk) {
            _log.Error($"failed to sync auto scale settings due to {r.ErrorV}");
        }

        // if the scaleset is touched during cleanup, don't continue to process it
        if (await _scaleSetOps.CleanupNodes(scaleset) is (true, _)) {
            _log.Verbose($"scaleset needed cleanup: {scaleset.ScalesetId}");
            return;
        }

        scaleset = await _scaleSetOps.SyncScalesetSize(scaleset);
        _ = await _scaleSetOps.ProcessStateUpdate(scaleset);
    }


    [Function("TimerWorkers")]
    public async Async.Task Run([TimerTrigger("00:01:30")] TimerInfo t) {
        // NOTE: Update pools first, such that scalesets impacted by pool updates
        // (such as shutdown or resize) happen during this iteration `timer_worker`
        // rather than the following iteration.

        var pools = _poolOps.SearchAll();
        await foreach (var pool in pools) {
            if (PoolStateHelper.NeedsWork.Contains(pool.State)) {
                _log.Info($"update pool: {pool.PoolId} ({pool.Name})");
                _ = await _poolOps.ProcessStateUpdate(pool);
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
            _log.Info($"update node: {node.MachineId}");
            _ = await _nodeOps.ProcessStateUpdate(node);
        }

        var scalesets = _scaleSetOps.SearchAll();
        await foreach (var scaleset in scalesets) {
            await ProcessScalesets(scaleset);
        }
    }
}
