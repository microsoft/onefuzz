using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service.Functions;

public class TimerWorkers {
    ILogger _log;
    IScalesetOperations _scaleSetOps;
    IPoolOperations _poolOps;
    INodeOperations _nodeOps;

    public TimerWorkers(ILogger<TimerWorkers> log, IOnefuzzContext context) {
        _log = log;
        _scaleSetOps = context.ScalesetOperations;
        _poolOps = context.PoolOperations;
        _nodeOps = context.NodeOperations;
    }

    private async Async.Task<Service.Scaleset> ProcessScalesets(Service.Scaleset scaleset) {
        _log.LogDebug("checking scaleset for updates: {ScalesetId}", scaleset.ScalesetId);

        scaleset = await _scaleSetOps.UpdateConfigs(scaleset);
        var r = await _scaleSetOps.SyncAutoscaleSettings(scaleset);
        if (!r.IsOk) {
            _log.LogError("failed to sync auto scale settings {ScalesetId} due to {Error}", scaleset.ScalesetId, r.ErrorV);
        }

        // if the scaleset is touched during cleanup, don't continue to process it
        var (touched, ss) = await _scaleSetOps.CleanupNodes(scaleset);
        if (touched) {
            _log.LogDebug("scaleset needed cleanup: {ScalesetId}", scaleset.ScalesetId);
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

        // we do not expect there to be many pools that need work, process them all in parallel
        var pools = await _poolOps.SearchStates(states: PoolStateHelper.NeedsWork).ToListAsync();
        await Async.Task.WhenAll(pools.Select(async pool => {
            try {
                _log.LogInformation("updating pool: {PoolId} ({PoolName}) - state: {PoolState}", pool.PoolId, pool.Name, pool.State);
                var newPool = await _poolOps.ProcessStateUpdate(pool);
                _log.LogInformation("completed updating pool: {PoolId} ({PoolName}) - now in state {PoolState}", pool.PoolId, pool.Name, newPool.State);
            } catch (Exception ex) {
                _log.LogError(ex, "failed to process pool");
            }
        }));

        // NOTE: Nodes, and Scalesets should be processed in a consistent order such
        // during 'pool scale down' operations. This means that pools that are
        // scaling down will more likely remove from the same scalesets over time.
        // By more likely removing from the same scalesets, we are more likely to
        // get to empty scalesets, which can safely be deleted.

        await _nodeOps.MarkOutdatedNodes();
        await _nodeOps.CleanupBusyNodesWithoutWork();

        // process up to 10 nodes in parallel
        var nodes = _nodeOps.SearchStates(states: NodeStateHelper.NeedsWorkStates);
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 10 };
        await Parallel.ForEachAsync(nodes, parallelOptions, async (node, _cancel) => {
            try {
                _log.LogInformation("updating node: {MachineId} - state: {NodeState}", node.MachineId, node.State);
                var newNode = await _nodeOps.ProcessStateUpdate(node);
                _log.LogInformation("completed updating node: {MachineId} - now in state {NodeState}", node.MachineId, newNode.State);
            } catch (Exception ex) {
                _log.LogError(ex, "failed to process node");
            }
        });

        // we do not expect there to be many scalesets, process them all in parallel
        var scalesets = await _scaleSetOps.SearchAll().ToListAsync();
        await Async.Task.WhenAll(scalesets.Select(async scaleset => {
            try {
                _log.LogInformation("updating scaleset: {ScalesetId} - state: {ScalesetState}", scaleset.ScalesetId, scaleset.State);
                var newScaleset = await ProcessScalesets(scaleset);
                _log.LogInformation("completed updating scaleset: {ScalesetId} - now in state {ScalesetState}", scaleset.ScalesetId, newScaleset.State);
            } catch (Exception ex) {
                _log.LogError(ex, "failed to process scaleset");
            }
        }));
    }
}
