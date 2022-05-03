using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IScalesetOperations : IOrm<Scaleset> {
    IAsyncEnumerable<Scaleset> Search();

    public IAsyncEnumerable<Scaleset?> SearchByPool(string poolName);

    public Async.Task UpdateConfigs(Scaleset scaleSet);
}

public class ScalesetOperations : StatefulOrm<Scaleset, ScalesetState>, IScalesetOperations {
    const string SCALESET_LOG_PREFIX = "scalesets: ";
    ILogTracer _log;
    IPoolOperations _poolOps;
    IEvents _events;
    IExtensions _extensions;
    IVmssOperations _vmssOps;
    IQueue _queue;
    INodeOperations _nodeOps;

    public ScalesetOperations(IStorage storage, ILogTracer log, IServiceConfig config, IPoolOperations poolOps, IEvents events, IExtensions extensions, IVmssOperations vmssOps, IQueue queue, INodeOperations nodeOps)
        : base(storage, log, config) {
        _log = log;
        _poolOps = poolOps;
        _events = events;
        _extensions = extensions;
        _vmssOps = vmssOps;
        _queue = queue;
        _nodeOps = nodeOps;
    }

    public IAsyncEnumerable<Scaleset> Search() {
        return QueryAsync();
    }

    public IAsyncEnumerable<Scaleset> SearchByPool(string poolName) {
        return QueryAsync(filter: $"pool_name eq '{poolName}'");
    }


    async Async.Task SetState(Scaleset scaleSet, ScalesetState state) {
        if (scaleSet.State == state)
            return;

        if (scaleSet.State == ScalesetState.Halt)
            return;

        var updatedScaleSet = scaleSet with { State = state };
        var r = await this.Replace(updatedScaleSet);
        if (!r.IsOk) {
            _log.Error($"Failed to update scaleset {scaleSet.ScalesetId} when updating state from {scaleSet.State} to {state}");
        }

        if (state == ScalesetState.Resize) {
            await _events.SendEvent(
                new EventScalesetResizeScheduled(updatedScaleSet.ScalesetId, updatedScaleSet.PoolName, updatedScaleSet.Size)
            );
        } else {
            await _events.SendEvent(
                new EventScalesetStateUpdated(updatedScaleSet.ScalesetId, updatedScaleSet.PoolName, updatedScaleSet.State)
            );
        }
    }

    async Async.Task SetFailed(Scaleset scaleSet, Error error) {
        if (scaleSet.Error is not null)
            return;

        await SetState(scaleSet with { Error = error }, ScalesetState.CreationFailed);
        await _events.SendEvent(new EventScalesetFailed(scaleSet.ScalesetId, scaleSet.PoolName, error));
    }

    public async Async.Task UpdateConfigs(Scaleset scaleSet) {
        if (scaleSet == null) {
            _log.Warning("skipping update configs on scaleset, since scaleset is null");
            return;
        }
        if (scaleSet.State == ScalesetState.Halt) {
            _log.Info($"{SCALESET_LOG_PREFIX} not updating configs, scalest is set to be deleted. scaleset_id: {scaleSet.ScalesetId}");
            return;
        }
        if (!scaleSet.NeedsConfigUpdate) {
            _log.Verbose($"{SCALESET_LOG_PREFIX} config update no needed. scaleset_id: {scaleSet.ScalesetId}");
            return;
        }

        _log.Info($"{SCALESET_LOG_PREFIX} updating scalset configs. scalset_id: {scaleSet.ScalesetId}");

        var pool = await _poolOps.GetByName(scaleSet.PoolName);

        if (!pool.IsOk || pool.OkV is null) {
            _log.Error($"{SCALESET_LOG_PREFIX} unable to find pool during config update. pool:{scaleSet.PoolName}, scaleset_id:{scaleSet.ScalesetId}");
            await SetFailed(scaleSet, pool.ErrorV!);
            return;
        }

        var extensions = await _extensions.FuzzExtensions(pool.OkV, scaleSet);

        var res = await _vmssOps.UpdateExtensions(scaleSet.ScalesetId, extensions);

        if (!res.IsOk) {
            _log.Info($"{SCALESET_LOG_PREFIX} unable to update configs {string.Join(',', res.ErrorV.Errors!)}");
        }
    }


    public async Async.Task Halt(Scaleset scaleset) {
        var shrinkQueue = new ShrinkQueue(scaleset.ScalesetId, _queue, _log);
        await shrinkQueue.Delete();

        await foreach (var node in _nodeOps.SearchStates(scaleSetId: scaleset.ScalesetId)) {
            _log.Info($"{SCALESET_LOG_PREFIX} deleting node scaleset_id {scaleset.ScalesetId} machine_id {node.MachineId}");


        }
        //_nodeOps.


    }


    /// <summary>
    /// Cleanup scaleset nodes
    /// </summary>
    /// <param name="scaleSet"></param>
    /// <returns>true if scaleset got modified</returns>
    public async Async.Task<bool> CleanupNodes(Scaleset scaleSet) {
        _log.Info($"{SCALESET_LOG_PREFIX} cleaning up nodes. scaleset_id {scaleSet.ScalesetId}");

        if (scaleSet.State == ScalesetState.Halt) {
            _log.Info($"{SCALESET_LOG_PREFIX} halting scaleset scaleset_id {scaleSet.ScalesetId}");

            await Halt(scaleSet);

            return true;
        }

        throw new NotImplementedException();
    }


}
