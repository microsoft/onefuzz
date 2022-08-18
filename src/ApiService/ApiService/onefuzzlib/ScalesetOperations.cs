using System.Text.Json;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Monitor.Models;

namespace Microsoft.OneFuzz.Service;

public interface IScalesetOperations : IStatefulOrm<Scaleset, ScalesetState> {
    IAsyncEnumerable<Scaleset> Search();

    IAsyncEnumerable<Scaleset> SearchByPool(PoolName poolName);

    Async.Task UpdateConfigs(Scaleset scaleSet);

    Async.Task<OneFuzzResult<Scaleset>> GetById(Guid scalesetId);
    IAsyncEnumerable<Scaleset> GetByObjectId(Guid objectId);

    Async.Task<bool> CleanupNodes(Scaleset scaleSet);

    Async.Task SetSize(Scaleset scaleset, int size);

    Async.Task SyncScalesetSize(Scaleset scaleset);

    Async.Task<Scaleset> SetState(Scaleset scaleset, ScalesetState state);
    public Async.Task<List<ScalesetNodeState>> GetNodes(Scaleset scaleset);
    IAsyncEnumerable<Scaleset> SearchStates(IEnumerable<ScalesetState> states);
    Async.Task<Scaleset> SetShutdown(Scaleset scaleset, bool now);
    Async.Task<Scaleset> SetSize(Scaleset scaleset, long size);
}

public class ScalesetOperations : StatefulOrm<Scaleset, ScalesetState, ScalesetOperations>, IScalesetOperations {
    const string SCALESET_LOG_PREFIX = "scalesets: ";

    private readonly ILogTracer _log;

    public ScalesetOperations(ILogTracer log, IOnefuzzContext context)
        : base(log, context) {
        _log = log;

    }

    public IAsyncEnumerable<Scaleset> Search() {
        return QueryAsync();
    }

    public IAsyncEnumerable<Scaleset> SearchByPool(PoolName poolName) {
        return QueryAsync(Query.PartitionKey(poolName.String));
    }

    public async Async.Task SyncScalesetSize(Scaleset scaleset) {
        // # If our understanding of size is out of sync with Azure, resize the
        // # scaleset to match our understanding.
        if (scaleset.State != ScalesetState.Running) {
            return;
        }

        var size = await _context.VmssOperations.GetVmssSize(scaleset.ScalesetId);
        if (size is null) {
            _log.Info($"{SCALESET_LOG_PREFIX} scaleset is unavailable. scaleset_id: {scaleset.ScalesetId}");
            //#if the scaleset is missing, this is an indication the scaleset
            //# was manually deleted, rather than having OneFuzz delete it.  As
            //# such, we should go thruogh the process of deleting it.
            await SetShutdown(scaleset, now: true);
            return;
        }
        if (size != scaleset.Size) {
            //# Azure auto-scaled us or nodes were manually added/removed
            //# New node state will be synced in cleanup_nodes
            _log.Info($"{SCALESET_LOG_PREFIX} unexpected scaleset size, resizing. scaleset_id: {scaleset.ScalesetId} expected:{scaleset.Size} actual:{size}");

            scaleset = scaleset with { Size = size.Value };
            var replaceResult = await Update(scaleset);
            if (!replaceResult.IsOk) {
                _log.Error($"Failed to update scaleset size for scaleset {scaleset.ScalesetId} due to {replaceResult.ErrorV}");
            }
        }
    }

    public async Async.Task SetSize(Scaleset scaleset, int size) {
        // # no longer needing to resize
        if (scaleset is null)
            return;
        if (scaleset.State != ScalesetState.Resize)
            return;

        _log.Info($"{SCALESET_LOG_PREFIX} scaleset resize: scaleset_id:{scaleset.ScalesetId} size:{size}");

        var shrinkQueue = new ShrinkQueue(scaleset.ScalesetId, _context.Queue, _log);
        // # reset the node delete queue
        await shrinkQueue.Clear();

        //# just in case, always ensure size is within max capacity
        scaleset = scaleset with { Size = Math.Min(scaleset.Size, MaxSize(scaleset)) };

        // # Treat Azure knowledge of the size of the scaleset as "ground truth"
        var vmssSize = await _context.VmssOperations.GetVmssSize(scaleset.ScalesetId);
        if (vmssSize is null) {
            _log.Info($"{SCALESET_LOG_PREFIX} scaleset is unavailable. scaleset_id {scaleset.ScalesetId}");

            //#if the scaleset is missing, this is an indication the scaleset
            //# was manually deleted, rather than having OneFuzz delete it.  As
            //# such, we should go thruogh the process of deleting it.
            await SetShutdown(scaleset, now: true);
            return;
        } else if (scaleset.Size == vmssSize) {
            await ResizeEqual(scaleset);
        } else if (scaleset.Size > vmssSize) {
            await ResizeGrow(scaleset);
        } else {
            await ResizeShrink(scaleset, vmssSize - scaleset.Size);
        }
    }

    public async Async.Task<Scaleset> SetState(Scaleset scaleset, ScalesetState state) {
        if (scaleset.State == state) {
            return scaleset;
        }

        if (scaleset.State == ScalesetState.Halt) {
            // terminal state, unable to change
            // TODO: should this throw an exception instead?
            return scaleset;
        }

        var updatedScaleSet = scaleset with { State = state };
        var r = await Update(updatedScaleSet);
        if (!r.IsOk) {
            var msg = "Failed to update scaleset {scaleSet.ScalesetId} when updating state from {scaleSet.State} to {state}";
            _log.Error(msg);
            // TODO: this should really return OneFuzzResult but then that propagates up the call stack
            throw new Exception(msg);
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

        return scaleset;
    }

    async Async.Task<Scaleset> SetFailed(Scaleset scaleset, Error error) {
        if (scaleset.Error is not null) {
            // already has an error, don't overwrite it
            return scaleset;
        }

        var updatedScaleset = await SetState(scaleset with { Error = error }, ScalesetState.CreationFailed);

        await _context.Events.SendEvent(new EventScalesetFailed(scaleset.ScalesetId, scaleset.PoolName, error));
        return updatedScaleset;
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

        if (!pool.IsOk) {
            _log.Error($"{SCALESET_LOG_PREFIX} unable to find pool during config update. pool:{scaleSet.PoolName}, scaleset_id:{scaleSet.ScalesetId}");
            await SetFailed(scaleSet, pool.ErrorV);
            return;
        }

        var extensions = await _context.Extensions.FuzzExtensions(pool.OkV, scaleSet);

        var res = await _context.VmssOperations.UpdateExtensions(scaleSet.ScalesetId, extensions);

        if (!res.IsOk) {
            _log.Info($"{SCALESET_LOG_PREFIX} unable to update configs {string.Join(',', res.ErrorV.Errors!)}");
        }
    }

    public Async.Task<Scaleset> SetShutdown(Scaleset scaleset, bool now)
        => SetState(scaleset, now ? ScalesetState.Halt : ScalesetState.Shutdown);

    public async Async.Task<Scaleset> Setup(Scaleset scaleset) {
        //# TODO: How do we pass in SSH configs for Windows?  Previously
        //# This was done as part of the generated per-task setup script.
        _logTracer.Info($"{SCALESET_LOG_PREFIX} setup. scalset_id: {scaleset.ScalesetId}");

        var network = await Network.Create(scaleset.Region, _context);
        var networkId = await network.GetId();
        if (networkId is null) {
            _logTracer.Info($"{SCALESET_LOG_PREFIX} creating network. region: {scaleset.Region} scaleset_id:{scaleset.ScalesetId}");
            var result = await network.Create();
            if (!result.IsOk) {
                return await SetFailed(scaleset, result.ErrorV);
            }

            //TODO : why are we saving scaleset here ? 
            var r = await Update(scaleset);
            if (!r.IsOk) {
                _logTracer.Error($"Failed to save scaleset {scaleset.ScalesetId} due to {r.ErrorV}");
            }

            return scaleset;
        }

        if (scaleset.Auth is null) {
            _logTracer.Error($"Scaleset Auth is missing for scaleset {scaleset.ScalesetId}");
            return await SetFailed(scaleset, new Error(ErrorCode.UNABLE_TO_CREATE, new[] { "missing required auth" }));
        }

        var vmss = await _context.VmssOperations.GetVmss(scaleset.ScalesetId);

        if (vmss is null) {
            var pool = await _context.PoolOperations.GetByName(scaleset.PoolName);
            if (!pool.IsOk) {
                _logTracer.Error($"Failed to get pool by name {scaleset.PoolName} for scaleset: {scaleset.ScalesetId}");
                return await SetFailed(scaleset, pool.ErrorV);
            }

            _logTracer.Info($"{SCALESET_LOG_PREFIX} creating scaleset. scaleset_id {scaleset.ScalesetId}");
            var extensions = await _context.Extensions.FuzzExtensions(pool.OkV, scaleset);
            var result = await _context.VmssOperations.CreateVmss(
                            scaleset.Region,
                            scaleset.ScalesetId,
                            scaleset.VmSku,
                            scaleset.Size,
                            scaleset.Image,
                            networkId!,
                            scaleset.SpotInstances,
                            scaleset.EphemeralOsDisks,
                            extensions,
                            scaleset.Auth.Password,
                            scaleset.Auth.PublicKey,
                            scaleset.Tags);

            if (!result.IsOk) {
                _logTracer.Error($"Failed to create scaleset {scaleset.ScalesetId} due to {result.ErrorV}");
                return await SetFailed(scaleset, result.ErrorV);
            } else {
                // TODO: Link up auto scale resource with diagnostics
                _logTracer.Info($"{SCALESET_LOG_PREFIX} creating scaleset scaleset_id: {scaleset.ScalesetId}");
            }
        } else if (vmss.ProvisioningState == "Creating") {
            var result = TrySetIdentity(scaleset, vmss);
            if (!result.IsOk) {
                _logTracer.Warning($"Could not set identity due to: {result.ErrorV}");
            } else {
                scaleset = result.OkV;
            }
        } else {
            _logTracer.Info($"{SCALESET_LOG_PREFIX} scaleset running scaleset_id {scaleset.ScalesetId}");

            var autoScaling = await TryEnableAutoScaling(scaleset);

            if (!autoScaling.IsOk) {
                _logTracer.Error($"Failed to set auto-scaling for {scaleset.ScalesetId} due to {autoScaling.ErrorV}");
                return await SetFailed(scaleset, autoScaling.ErrorV);
            }

            var result = TrySetIdentity(scaleset, vmss);
            if (!result.IsOk) {
                _logTracer.Error($"Failed to set identity for scaleset {scaleset.ScalesetId} due to: {result.ErrorV}");
                return await SetFailed(scaleset, result.ErrorV);
            } else {
                scaleset = await SetState(scaleset, ScalesetState.Running);
            }
        }

        var rr = await Update(scaleset);
        if (!rr.IsOk) {
            _logTracer.Error($"Failed to save scale data for scale set: {scaleset.ScalesetId}");
        }

        return scaleset;
    }


    static OneFuzzResult<Scaleset> TrySetIdentity(Scaleset scaleset, VirtualMachineScaleSetData vmss) {
        if (scaleset.ClientObjectId is null) {
            return OneFuzzResult.Ok(scaleset);
        }

        if (vmss.Identity is not null && vmss.Identity.UserAssignedIdentities is not null) {
            if (vmss.Identity.UserAssignedIdentities.Count != 1) {
                return OneFuzzResult<Scaleset>.Error(ErrorCode.VM_CREATE_FAILED, "The scaleset is expected to have exactly 1 user assigned identity");
            }
            var principalId = vmss.Identity.UserAssignedIdentities.First().Value.PrincipalId;
            if (principalId is null) {
                return OneFuzzResult<Scaleset>.Error(ErrorCode.VM_CREATE_FAILED, "The scaleset principal ID is null");
            }
            return OneFuzzResult<Scaleset>.Ok(scaleset with { ClientObjectId = principalId });
        } else {
            return OneFuzzResult<Scaleset>.Error(ErrorCode.VM_CREATE_FAILED, "The scaleset identity is null");
        }
    }

    async Async.Task<OneFuzzResultVoid> TryEnableAutoScaling(Scaleset scaleset) {
        _logTracer.Info($"Trying to add auto scaling for scaleset {scaleset.ScalesetId}");

        var r = await _context.PoolOperations.GetByName(scaleset.PoolName);
        if (!r.IsOk) {
            _logTracer.Error($"Failed to get pool by name: {scaleset.PoolName} error: {r.ErrorV}");
            return r.ErrorV;
        }
        var pool = r.OkV;
        var poolQueueId = _context.PoolOperations.GetPoolQueue(pool.PoolId);
        var poolQueueUri = _context.Queue.GetResourceId(poolQueueId, StorageType.Corpus);

        var capacity = await _context.VmssOperations.GetVmssSize(scaleset.ScalesetId);

        if (!capacity.HasValue) {
            var capacityFailed = OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_FIND, $"Failed to get capacity for scaleset {scaleset.ScalesetId}");
            _logTracer.Error(capacityFailed.ErrorV.ToString());
            return capacityFailed;
        }

        var autoScaleConfig = await _context.AutoScaleOperations.GetSettingsForScaleset(scaleset.ScalesetId);

        if (poolQueueUri is null) {
            var failedToFindQueueUri = OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_FIND, $"Failed to get pool queue uri for scaleset {scaleset.ScalesetId}");
            _logTracer.Error(failedToFindQueueUri.ErrorV.ToString());
            return failedToFindQueueUri;
        }

        AutoscaleProfile autoScaleProfile;
        if (autoScaleConfig is null) {
            autoScaleProfile = _context.AutoScaleOperations.DefaultAutoScaleProfile(poolQueueUri!, capacity.Value);
        } else {
            _logTracer.Info("Using existing auto scale settings from database");
            autoScaleProfile = _context.AutoScaleOperations.CreateAutoScaleProfile(
                    poolQueueUri!,
                    autoScaleConfig.Min,
                    autoScaleConfig.Max,
                    autoScaleConfig.Default,
                    autoScaleConfig.ScaleOutAmount,
                    autoScaleConfig.ScaleOutCooldown,
                    autoScaleConfig.ScaleInAmount,
                    autoScaleConfig.ScaleInCooldown
                );

        }

        _logTracer.Info($"Added auto scale resource to scaleset: {scaleset.ScalesetId}");
        return await _context.AutoScaleOperations.AddAutoScaleToVmss(scaleset.ScalesetId, autoScaleProfile);
    }


    public async Async.Task<Scaleset> Init(Scaleset scaleset) {
        _logTracer.Info($"{SCALESET_LOG_PREFIX} init. scaleset_id:{scaleset.ScalesetId}");
        var shrinkQueue = new ShrinkQueue(scaleset.ScalesetId, _context.Queue, _logTracer);
        await shrinkQueue.Create();
        // Handle the race condition between a pool being deleted and a
        // scaleset being added to the pool.

        var poolResult = await _context.PoolOperations.GetByName(scaleset.PoolName);
        if (!poolResult.IsOk) {
            _logTracer.Error($"Failed to get pool by name {scaleset.PoolName} for scaleset: {scaleset.ScalesetId} due to {poolResult.ErrorV}");
            return await SetFailed(scaleset, poolResult.ErrorV);
        }

        var pool = poolResult.OkV;

        if (pool.State == PoolState.Init) {
            _logTracer.Info($"{SCALESET_LOG_PREFIX} waiting for pool. pool_name:{scaleset.PoolName} scaleset_id:{scaleset.ScalesetId}");
        } else if (pool.State == PoolState.Running) {
            var imageOsResult = await _context.ImageOperations.GetOs(scaleset.Region, scaleset.Image);
            if (!imageOsResult.IsOk) {
                _logTracer.Error($"Failed to get OS with region: {scaleset.Region} image:{scaleset.Image} for scaleset: {scaleset.ScalesetId} due to {imageOsResult.ErrorV}");
                return await SetFailed(scaleset, imageOsResult.ErrorV);
            } else if (imageOsResult.OkV != pool.Os) {
                _logTracer.Error($"Got invalid OS: {imageOsResult.OkV} for scaleset: {scaleset.ScalesetId} expected OS {pool.Os}");
                return await SetFailed(scaleset, new Error(ErrorCode.INVALID_REQUEST, new[] { $"invalid os (got: {imageOsResult.OkV} needed: {pool.Os})" }));
            } else {
                return await SetState(scaleset, ScalesetState.Setup);
            }
        } else {
            return await SetState(scaleset, ScalesetState.Setup);
        }

        return scaleset;
    }

    public async Async.Task<Scaleset> Halt(Scaleset scaleset) {
        var shrinkQueue = new ShrinkQueue(scaleset.ScalesetId, _context.Queue, _log);
        await shrinkQueue.Delete();

        await foreach (var node in _context.NodeOperations.SearchStates(scalesetId: scaleset.ScalesetId)) {
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
            var r = await Update(scaleset);
            if (!r.IsOk) {
                _log.WithHttpStatus(r.ErrorV).Error($"Failed to save scaleset record {scaleset.ScalesetId}");
            }
        }

        return scaleset;
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
        var nodes = _context.NodeOperations.SearchStates(scalesetId: scaleSet.ScalesetId);

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

        await ReimageNodes(scaleSet, toReimage.Values, strategy);
        await DeleteNodes(scaleSet, toDelete.Values, strategy);

        return toReimage.Count > 0 || toDelete.Count > 0;
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


    private async Async.Task ResizeEqual(Scaleset scaleset) {
        //# NOTE: this is the only place we reset to the 'running' state.
        //# This ensures that our idea of scaleset size agrees with Azure

        var nodeCount = await _context.NodeOperations.SearchStates(scalesetId: scaleset.ScalesetId).CountAsync();
        if (nodeCount == scaleset.Size) {
            _log.Info($"{SCALESET_LOG_PREFIX} resize finished: {scaleset.ScalesetId}");
            await SetState(scaleset, ScalesetState.Running);
        } else {
            _log.Info($"{SCALESET_LOG_PREFIX} resize finished, waiting for nodes to check in. scaleset_id: {scaleset.ScalesetId} ({nodeCount} of {scaleset.Size} checked in)");
        }
    }

    private async Async.Task ResizeGrow(Scaleset scaleset) {

        var resizeResult = await _context.VmssOperations.ResizeVmss(scaleset.ScalesetId, scaleset.Size);
        if (resizeResult.IsOk == false) {
            _log.Info($"{SCALESET_LOG_PREFIX} scaleset is mid-operation already scaleset_id: {scaleset.ScalesetId} message: {resizeResult.ErrorV}");
        }
    }

    private async Async.Task ResizeShrink(Scaleset scaleset, long? toRemove) {
        _log.Info($"{SCALESET_LOG_PREFIX} shrinking scaleset. scaleset_id: {scaleset.ScalesetId} to remove {toRemove}");

        if (!toRemove.HasValue) {
            return;
        } else {
            var queue = new ShrinkQueue(scaleset.ScalesetId, _context.Queue, _log);
            await queue.SetSize(toRemove.Value);
            var nodes = _context.NodeOperations.SearchStates(scalesetId: scaleset.ScalesetId);
            await foreach (var node in nodes) {
                await _context.NodeOperations.SendStopIfFree(node);
            }
        }
    }

    public async Task<List<ScalesetNodeState>> GetNodes(Scaleset scaleset) {
        // Be in at-least 'setup' before checking for the list of VMs
        if (scaleset.State == ScalesetState.Init) {
            return new List<ScalesetNodeState>();
        }

        var (nodes, azureNodes) = await (
            _context.NodeOperations.SearchStates(scaleset.ScalesetId).ToListAsync().AsTask(),
            _context.VmssOperations.ListInstanceIds(scaleset.ScalesetId));

        var result = new List<ScalesetNodeState>();
        foreach (var (machineId, instanceId) in azureNodes) {
            var node = nodes.FirstOrDefault(n => n.MachineId == machineId);
            result.Add(new ScalesetNodeState(
                MachineId: machineId,
                InstanceId: instanceId,
                node?.State));
        }

        return result;
    }

    public IAsyncEnumerable<Scaleset> SearchStates(IEnumerable<ScalesetState> states)
        => QueryAsync(Query.EqualAnyEnum("state", states));

    public Async.Task<Scaleset> SetSize(Scaleset scaleset, long size) {
        var permittedSize = Math.Min(size, MaxSize(scaleset));
        if (permittedSize == scaleset.Size) {
            return Async.Task.FromResult(scaleset); // nothing to do
        }

        scaleset = scaleset with { Size = permittedSize };
        return SetState(scaleset, ScalesetState.Resize);
    }

    public async Async.Task<Scaleset> Shutdown(Scaleset scaleset) {
        var size = await _context.VmssOperations.GetVmssSize(scaleset.ScalesetId);
        if (size == null) {
            _logTracer.Info($"{SCALESET_LOG_PREFIX} scale set shutdown: scaleset already deleted - scaleset_id:{scaleset.ScalesetId}");
            return await Halt(scaleset);
        }

        _logTracer.Info($"{SCALESET_LOG_PREFIX} scaleset shutdown: scaleset_id:{scaleset.ScalesetId} size:{size}");
        var nodes = _context.NodeOperations.SearchStates(scalesetId: scaleset.ScalesetId);
        // TODO: Parallelization opportunity
        await foreach (var node in nodes) {
            await _context.NodeOperations.SetShutdown(node);
        }

        _logTracer.Info($"{SCALESET_LOG_PREFIX} checking for existing auto scale settings {scaleset.ScalesetId}");

        var autoScalePolicy = _context.AutoScaleOperations.GetAutoscaleSettings(scaleset.ScalesetId);
        if (autoScalePolicy.IsOk && autoScalePolicy.OkV != null) {
            foreach (var profile in autoScalePolicy.OkV.Data.Profiles) {
                var queueUri = profile.Rules.First().MetricTrigger.MetricResourceId;

                // Overwrite any existing scaling rules with one that will
                //   try to scale in by 1 node at every opportunity
                profile.Rules.Clear();
                profile.Rules.Add(AutoScaleOperations.ShutdownScalesetRule(queueUri));

                // Auto scale (the azure service) will not allow you to
                //   set the minimum number of instances to a number
                //   smaller than the number of instances
                //   with scale in protection enabled.
                //
                // Since:
                //   * Nodes can no longer pick up work once the scale set is
                //       in `shutdown` state
                //   * All scale out rules are removed
                // Then: The number of nodes in the scale set with scale in
                //   protection enabled _must_ strictly decrease over time.
                //
                //  This guarantees that _eventually_
                //   auto scale will scale in the remaining nodes,
                //   the scale set will have 0 instances,
                //   and once the scale set is empty, we will delete it.
                _logTracer.Info($"{SCALESET_LOG_PREFIX} Getting nodes with scale in protection");
                var vmsWithProtection = await _context.VmssOperations.ListVmss(
                    scaleset.ScalesetId,
                    (vmResource) => vmResource.Data.ProtectionPolicy.ProtectFromScaleIn.HasValue && vmResource.Data.ProtectionPolicy.ProtectFromScaleIn.Value
                );

                _logTracer.Info($"{SCALESET_LOG_PREFIX} {JsonSerializer.Serialize(vmsWithProtection)}");
                if (vmsWithProtection != null && vmsWithProtection.Any()) {
                    var numVmsWithProtection = vmsWithProtection.Count;
                    profile.Capacity.Minimum = numVmsWithProtection.ToString();
                    profile.Capacity.Default = numVmsWithProtection.ToString();
                } else {
                    _logTracer.Error($"Failed to list vmss for scaleset {scaleset.ScalesetId}");
                }
            }

            var updatedAutoScale = await _context.AutoScaleOperations.UpdateAutoscale(autoScalePolicy.OkV.Data);
            if (!updatedAutoScale.IsOk) {
                _logTracer.Error($"Failed to update auto scale {updatedAutoScale}");
            }
        } else if (!autoScalePolicy.IsOk) {
            _logTracer.Error(autoScalePolicy.ErrorV);
        } else {
            _logTracer.Info($"No existing auto scale settings found for {scaleset.ScalesetId}");
        }

        if (size == 0) {
            return await Halt(scaleset);
        }

        return scaleset;
    }

    private static long MaxSize(Scaleset scaleset) {
        // https://docs.microsoft.com/en-us/azure/virtual-machine-scale-sets/virtual-machine-scale-sets-placement-groups#checklist-for-using-large-scale-sets
        if (scaleset.Image.StartsWith("/", StringComparison.Ordinal)) {
            return 600;
        } else {
            return 1000;
        }
    }
}
