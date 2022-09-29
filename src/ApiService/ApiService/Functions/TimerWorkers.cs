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


    [Function("TimerWorkers")]
    public async Async.Task Run([TimerTrigger("00:01:30")] TimerInfo t) {
        // NOTE: Update pools first, such that scalesets impacted by pool updates
        // (such as shutdown or resize) happen during this iteration `timer_worker`
        // rather than the following iteration.

        var pools = _poolOps.SearchStates(states: PoolStateHelper.NeedsWork);
        await foreach (var pool in pools) {
            try {
                _log.Info($"updating pool: {pool.PoolId:Tag:PoolId} ({pool.Name:Tag:PoolName}) - state: {pool.State:Tag:PoolState}");
                var newPool = await _poolOps.ProcessStateUpdate(pool);
                _log.Info($"completed updating pool - now in state {newPool.State:Tag:PoolState}");
            } catch (Exception ex) {
                _log.Exception(ex, $"failed to process pool");
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
            try {
                _log.Info($"updating node: {node.MachineId:Tag:MachineId} - state: {node.State:Tag:NodeState}");
                var newNode = await _nodeOps.ProcessStateUpdate(node);
                _log.Info($"completed updating node - now in state {newNode.State:Tag:NodeState}");
            } catch (Exception ex) {
                _log.Exception(ex, $"failed to process node");
            }
        }

        var scalesets = _scaleSetOps.SearchAll();
        await foreach (var scaleset in scalesets) {
            try {
                _log.Info($"updating scaleset: {scaleset.ScalesetId:Tag:ScalesetId} - state: {scaleset.State:Tag:ScalesetState}");
                var newScaleset = await ProcessScalesets(scaleset);
                _log.Info($"completed updating scaleset - now in state {newScaleset.State:Tag:ScalesetState}");
            } catch (Exception ex) {
                _log.Exception(ex, $"failed to process scaleset");
            }
        }
    }
}
