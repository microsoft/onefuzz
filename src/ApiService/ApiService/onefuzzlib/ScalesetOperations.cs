using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IScalesetOperations : IOrm<Scaleset> {
    IAsyncEnumerable<Scaleset> Search();

    public IAsyncEnumerable<Scaleset?> SearchByPool(string poolName);

    public Async.Task UpdateConfigs(Scaleset scaleSet);

    public Async.Task<OneFuzzResult<Scaleset>> GetById(Guid scalesetId);
    IAsyncEnumerable<Scaleset> GetByObjectId(Guid objectId);
}

public class ScalesetOperations : StatefulOrm<Scaleset, ScalesetState>, IScalesetOperations {
    const string SCALESET_LOG_PREFIX = "scalesets: ";

    ILogTracer _log;

    public ScalesetOperations(ILogTracer log, IOnefuzzContext context)
        : base(log, context) {
        _log = log;

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
            await _context.Events.SendEvent(
                new EventScalesetResizeScheduled(updatedScaleSet.ScalesetId, updatedScaleSet.PoolName, updatedScaleSet.Size)
            );
        } else {
            await _context.Events.SendEvent(
                new EventScalesetStateUpdated(updatedScaleSet.ScalesetId, updatedScaleSet.PoolName, updatedScaleSet.State)
            );
        }
    }

    async Async.Task SetFailed(Scaleset scaleSet, Error error) {
        if (scaleSet.Error is not null)
            return;

        await SetState(scaleSet with { Error = error }, ScalesetState.CreationFailed);
        await _context.Events.SendEvent(new EventScalesetFailed(scaleSet.ScalesetId, scaleSet.PoolName, error));
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

        var pool = await _context.PoolOperations.GetByName(scaleSet.PoolName);

        if (!pool.IsOk || pool.OkV is null) {
            _log.Error($"{SCALESET_LOG_PREFIX} unable to find pool during config update. pool:{scaleSet.PoolName}, scaleset_id:{scaleSet.ScalesetId}");
            await SetFailed(scaleSet, pool.ErrorV!);
            return;
        }

        var extensions = await _context.Extensions.FuzzExtensions(pool.OkV, scaleSet);

        var res = await _context.VmssOperations.UpdateExtensions(scaleSet.ScalesetId, extensions);

        if (!res.IsOk) {
            _log.Info($"{SCALESET_LOG_PREFIX} unable to update configs {string.Join(',', res.ErrorV.Errors!)}");
        }
    }


    public async Async.Task Halt(Scaleset scaleset) {
        var shrinkQueue = new ShrinkQueue(scaleset.ScalesetId, _context.Queue, _log);
        await shrinkQueue.Delete();

        await foreach (var node in _context.NodeOperations.SearchStates(scaleSetId: scaleset.ScalesetId)) {
            _log.Info($"{SCALESET_LOG_PREFIX} deleting node scaleset_id {scaleset.ScalesetId} machine_id {node.MachineId}");
            await _context.NodeOperations.Delete(node);
        }
        _log.Info($"{SCALESET_LOG_PREFIX} scaleset delete starting: scaleset_id:{scaleset.ScalesetId}");

        if (await _context.VmssOperations.DeleteVmss(scaleset.ScalesetId)) {
            _log.Info($"{SCALESET_LOG_PREFIX}scaleset deleted: scaleset_id {scaleset.ScalesetId}");
            var r = await Delete(scaleset);
            if (!r.IsOk) {
                _log.WithHttpStatus(r.ErrorV).Error($"Failed to delete scaleset record {scaleset.ScalesetId}");
            }
        } else {
            var r = await Replace(scaleset);
            if (!r.IsOk) {
                _log.WithHttpStatus(r.ErrorV).Error($"Failed to save scaleset record {scaleset.ScalesetId}");
            }
        }
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

        var pool = await _context.PoolOperations.GetByName(scaleSet.PoolName);
        if (!pool.IsOk) {
            _log.Error($"unable to find pool during cleanup {scaleSet.ScalesetId} - {scaleSet.PoolName}");
            await SetFailed(scaleSet, pool.ErrorV!);
            return true;
        }
        await _context.NodeOperations.ReimageLongLivedNodes(scaleSet.ScalesetId);

        //ground truth of existing nodes
        var azureNodes = await _context.VmssOperations.ListInstanceIds(scaleSet.ScalesetId);
        var nodes = _context.NodeOperations.SearchStates(scaleSetId: scaleSet.ScalesetId);

        //# Nodes do not exists in scalesets but in table due to unknown failure
        await foreach (var node in nodes) {
            if (!azureNodes.ContainsKey(node.MachineId)) {
                _log.Info($"{SCALESET_LOG_PREFIX} no longer in scaleset. scaleset_id:{scaleSet.ScalesetId} machine_id:{node.MachineId}");
                await _context.NodeOperations.Delete(node);
            }
        }

        //# Scalesets can have nodes that never check in (such as broken OS setup
        //# scripts).
        //
        //# This will add nodes that Azure knows about but have not checked in
        //# such that the `dead node` detection will eventually reimage the node.
        //
        //# NOTE: If node setup takes longer than NODE_EXPIRATION_TIME (1 hour),
        //# this will cause the nodes to continuously get reimaged.
        var nodeMachineIds = await nodes.Select(x => x.MachineId).ToHashSetAsync();

        foreach (var azureNode in azureNodes) {
            var machineId = azureNode.Key;

            if (nodeMachineIds.Contains(machineId)) {
                continue;
            }
            _log.Info($"{SCALESET_LOG_PREFIX} adding missing azure node. scaleset_id:{scaleSet.ScalesetId} machine_id:{machineId}");

            //# Note, using `new=True` makes it such that if a node already has
            //# checked in, this won't overwrite it.

            //Python code does use created node
            //pool.IsOk was handled above, OkV must be not null at this point
            var _ = await _context.NodeOperations.Create(pool.OkV!.PoolId, scaleSet.PoolName, machineId, scaleSet.ScalesetId, _context.ServiceConfiguration.OneFuzzVersion, true);
        }

        var existingNodes =
                from x in nodes
                where azureNodes.ContainsKey(x.MachineId)
                select x;

        var nodesToReset =
                from x in existingNodes
                where x.State.ReadyForReset()
                select x;


        Dictionary<Guid, Node> toDelete = new();
        Dictionary<Guid, Node> toReimage = new();

        await foreach (var node in nodesToReset) {
            if (node.DeleteRequested) {
                toDelete[node.MachineId] = node;
            } else {
                if (await new ShrinkQueue(scaleSet.ScalesetId, _context.Queue, _log).ShouldShrink()) {
                    await _context.NodeOperations.SetHalt(node);
                    toDelete[node.MachineId] = node;
                } else if (await new ShrinkQueue(pool.OkV!.PoolId, _context.Queue, _log).ShouldShrink()) {
                    await _context.NodeOperations.SetHalt(node);
                    toDelete[node.MachineId] = node;
                } else {
                    toReimage[node.MachineId] = node;
                }
            }
        }

        var deadNodes = _context.NodeOperations.GetDeadNodes(scaleSet.ScalesetId, INodeOperations.NODE_EXPIRATION_TIME);

        await foreach (var deadNode in deadNodes) {
            string errorMessage;
            if (deadNode.Heartbeat is not null) {
                errorMessage = "node reimaged due to expired hearbeat";
            } else {
                errorMessage = "node reimaged due to never receiving heartbeat";
            }

            var error = new Error(ErrorCode.TASK_FAILED, new[] { $"{errorMessage} scaleset_id {deadNode.ScalesetId} last heartbeat:{deadNode.Heartbeat}" });
            await _context.NodeOperations.MarkTasksStoppedEarly(deadNode, error);
            await _context.NodeOperations.ToReimage(deadNode, true);
            toReimage[deadNode.MachineId] = deadNode;
        }

        // Perform operations until they fail due to scaleset getting locked
        NodeDisposalStrategy strategy =
            (_context.ServiceConfiguration.OneFuzzNodeDisposalStrategy.ToLowerInvariant()) switch {
                "decomission" => NodeDisposalStrategy.Decomission,
                _ => NodeDisposalStrategy.ScaleIn
            };

        throw new NotImplementedException();
    }


    public async Async.Task ReimageNodes(Scaleset scaleSet, IEnumerable<Node> nodes, NodeDisposalStrategy disposalStrategy) {

        if (nodes is null || !nodes.Any()) {
            _log.Info($"{SCALESET_LOG_PREFIX} no nodes to reimage: scaleset_id: {scaleSet.ScalesetId}");
            return;
        }

        if (scaleSet.State == ScalesetState.Shutdown) {
            _log.Info($"{SCALESET_LOG_PREFIX} scaleset shutting down, deleting rather than reimaging nodes. scaleset_id: {scaleSet.ScalesetId}");
            await DeleteNodes(scaleSet, nodes, disposalStrategy);
            return;
        }

        if (scaleSet.State == ScalesetState.Halt) {
            _log.Info($"{SCALESET_LOG_PREFIX} scaleset halting, ignoring node reimage: scaleset_id:{scaleSet.ScalesetId}");
            return;
        }

        var machineIds = new HashSet<Guid>();
        foreach (var node in nodes) {
            if (node.State == NodeState.Done) {
                continue;
            }

            if (node.DebugKeepNode) {
                _log.Warning($"{SCALESET_LOG_PREFIX} not reimaging manually overriden node. scaleset_id:{scaleSet.ScalesetId} machine_id:{node.MachineId}");
            } else {
                machineIds.Add(node.MachineId);
            }
        }

        if (!machineIds.Any()) {
            _log.Info($"{SCALESET_LOG_PREFIX} no nodes to reimage: {scaleSet.ScalesetId}");
            return;
        }

        throw new NotImplementedException();
    }

    public async Async.Task DeleteNodes(Scaleset scaleSet, IEnumerable<Node> nodes, NodeDisposalStrategy disposalStrategy) {
        if (nodes is null || !nodes.Any()) {
            _log.Info($"{SCALESET_LOG_PREFIX} no nodes to delete: scaleset_id: {scaleSet.ScalesetId}");
            return;
        }


        foreach (var node in nodes) {
            await _context.NodeOperations.SetHalt(node);
        }

        if (scaleSet.State == ScalesetState.Halt) {
            _log.Info($"{SCALESET_LOG_PREFIX} scaleset halting, ignoring deletion {scaleSet.ScalesetId}");
            return;
        }

        HashSet<Guid> machineIds = new();

        foreach (var node in nodes) {
            if (node.DebugKeepNode) {
                _log.Warning($"{SCALESET_LOG_PREFIX} not deleting manually overriden node. scaleset_id:{scaleSet.ScalesetId} machine_id:{node.MachineId}");
            } else {
                machineIds.Add(node.MachineId);
            }
        }

        throw new NotImplementedException();
    }

    public async Task<OneFuzzResult<Scaleset>> GetById(Guid scalesetId) {
        var data = QueryAsync(filter: $"RowKey eq '{scalesetId}'");
        var count = await data.CountAsync();
        if (data == null || count == 0) {
            return OneFuzzResult<Scaleset>.Error(
                ErrorCode.INVALID_REQUEST,
                "unable to find scaleset"
            );
        }

        if (count != 1) {
            return OneFuzzResult<Scaleset>.Error(
                ErrorCode.INVALID_REQUEST,
                "error identifying scaleset"
            );
        }

        return OneFuzzResult<Scaleset>.Ok(await data.SingleAsync());
    }

    public IAsyncEnumerable<Scaleset> GetByObjectId(Guid objectId) {
        return QueryAsync(filter: $"client_object_id eq '{objectId}'");
    }
}
