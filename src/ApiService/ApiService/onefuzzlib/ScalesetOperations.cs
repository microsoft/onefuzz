using System.Text.Json;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Monitor.Models;
namespace Microsoft.OneFuzz.Service;

public interface IScalesetOperations : IStatefulOrm<Scaleset, ScalesetState> {
    IAsyncEnumerable<Scaleset> Search();

    IAsyncEnumerable<Scaleset> SearchByPool(PoolName poolName);

    Async.Task<Scaleset> UpdateConfigs(Scaleset scaleSet);

    Async.Task<OneFuzzResult<Scaleset>> GetById(Guid scalesetId);
    IAsyncEnumerable<Scaleset> GetByObjectId(Guid objectId);

    Async.Task<(bool, Scaleset)> CleanupNodes(Scaleset scaleSet);

    Async.Task<Scaleset> SyncScalesetSize(Scaleset scaleset);

    Async.Task<Scaleset> SetState(Scaleset scaleset, ScalesetState state);
    public Async.Task<List<ScalesetNodeState>> GetNodes(Scaleset scaleset);
    IAsyncEnumerable<Scaleset> SearchStates(IEnumerable<ScalesetState> states);
    Async.Task<Scaleset> SetShutdown(Scaleset scaleset, bool now);
    Async.Task<Scaleset> SetSize(Scaleset scaleset, long size);

    // state transitions:
    Async.Task<Scaleset> Init(Scaleset scaleset);
    Async.Task<Scaleset> Setup(Scaleset scaleset);
    Async.Task<Scaleset> Resize(Scaleset scaleset);
    Async.Task<Scaleset> Running(Scaleset scaleset);
    Async.Task<Scaleset> Shutdown(Scaleset scaleset);
    Async.Task<Scaleset> Halt(Scaleset scaleset);
    Async.Task<Scaleset> CreationFailed(Scaleset scaleset);

    Async.Task<OneFuzzResultVoid> SyncAutoscaleSettings(Scaleset scaleset);
}

public class ScalesetOperations : StatefulOrm<Scaleset, ScalesetState, ScalesetOperations>, IScalesetOperations {
    private readonly ILogTracer _log;

    public ScalesetOperations(ILogTracer log, IOnefuzzContext context)
        : base(log.WithTag("Component", "scalesets"), context) {
        _log = base._logTracer;

    }

    public IAsyncEnumerable<Scaleset> Search() {
        return QueryAsync();
    }

    public IAsyncEnumerable<Scaleset> SearchByPool(PoolName poolName) {
        return QueryAsync(Query.PartitionKey(poolName.String));
    }

    public async Async.Task<Scaleset> SyncScalesetSize(Scaleset scaleset) {
        // # If our understanding of size is out of sync with Azure, resize the
        // # scaleset to match our understanding.
        if (scaleset.State != ScalesetState.Running) {
            return scaleset;
        }

        var size = await _context.VmssOperations.GetVmssSize(scaleset.ScalesetId);
        if (size is null) {
            _log.Info($"scaleset is unavailable {scaleset.ScalesetId:Tag:ScalesetId}");
            //#if the scaleset is missing, this is an indication the scaleset
            //# was manually deleted, rather than having OneFuzz delete it.  As
            //# such, we should go thruogh the process of deleting it.
            scaleset = await SetShutdown(scaleset, now: true);
            return scaleset;
        }

        if (size != scaleset.Size) {
            //# Azure auto-scaled us or nodes were manually added/removed
            //# New node state will be synced in cleanup_nodes
            _log.Info($"unexpected scaleset size, resizing {scaleset.ScalesetId:Tag:ScalesetId} {scaleset.Size:Tag:ExpectedSize} {size:Tag:ActualSize}");

            scaleset = scaleset with { Size = size.Value };
            var replaceResult = await Replace(scaleset);
            if (!replaceResult.IsOk) {
                _log.WithHttpStatus(replaceResult.ErrorV).Error($"failed to update scaleset size for {scaleset.ScalesetId:Tag:ScalesetId}");
            }
        }

        return scaleset;
    }

    public async Async.Task<OneFuzzResultVoid> SyncAutoscaleSettings(Scaleset scaleset) {
        if (scaleset.State != ScalesetState.Running)
            return OneFuzzResultVoid.Ok;

        _log.Info($"syncing auto-scale settings for scaleset {scaleset.ScalesetId:Tag:ScalesetId}");

        var autoscaleProfile = await _context.AutoScaleOperations.GetAutoScaleProfile(scaleset.ScalesetId);
        if (!autoscaleProfile.IsOk) {
            return autoscaleProfile.ErrorV;
        }
        var profile = autoscaleProfile.OkV;

        var minAmount = Int64.Parse(profile.Capacity.Minimum);
        var maxAmount = Int64.Parse(profile.Capacity.Maximum);
        var defaultAmount = Int64.Parse(profile.Capacity.Default);

        var scaleOutAmount = 1;
        var scaleOutCooldown = 10L;
        var scaleInAmount = 1;
        var scaleInCooldown = 15L;

        foreach (var rule in profile.Rules) {
            var scaleAction = rule.ScaleAction;

            if (scaleAction.Direction == ScaleDirection.Increase) {
                scaleOutAmount = Int32.Parse(scaleAction.Value);
                _logTracer.Info($"Scaleout cooldown in seconds. {scaleOutCooldown:Tag:Before}");
                scaleOutCooldown = (long)scaleAction.Cooldown.TotalMinutes;
                _logTracer.Info($"Scaleout cooldown in seconds. {scaleOutCooldown:Tag:After}");
            } else if (scaleAction.Direction == ScaleDirection.Decrease) {
                scaleInAmount = Int32.Parse(scaleAction.Value);
                _logTracer.Info($"Scalin cooldown in seconds. {scaleInCooldown:Tag:Before}");
                scaleInCooldown = (long)scaleAction.Cooldown.TotalMinutes;
                _logTracer.Info($"Scalein cooldown in seconds. {scaleInCooldown:Tag:After}");
            } else {
                continue;
            }
        }

        var poolResult = await _context.PoolOperations.GetByName(scaleset.PoolName);
        if (!poolResult.IsOk) {
            return poolResult.ErrorV;
        }

        _logTracer.Info($"Updating auto-scale entry for scaleset: {scaleset.ScalesetId:Tag:ScalesetId}");
        _ = await _context.AutoScaleOperations.Update(
                    scalesetId: scaleset.ScalesetId,
                    minAmount: minAmount,
                    maxAmount: maxAmount,
                    defaultAmount: defaultAmount,
                    scaleOutAmount: scaleOutAmount,
                    scaleOutCooldown: scaleOutCooldown,
                    scaleInAmount: scaleInAmount,
                    scaleInCooldown: scaleInCooldown);

        return OneFuzzResultVoid.Ok;
    }

    public async Async.Task<Scaleset> Resize(Scaleset scaleset) {

        if (scaleset.State != ScalesetState.Resize) {
            return scaleset;
        }

        _log.Info($"scaleset resize: {scaleset.ScalesetId:Tag:ScalesetId} - {scaleset.Size:Tag:Size}");

        var shrinkQueue = new ShrinkQueue(scaleset.ScalesetId, _context.Queue, _log);
        // # reset the node delete queue
        await shrinkQueue.Clear();

        //# just in case, always ensure size is within max capacity
        scaleset = scaleset with { Size = Math.Min(scaleset.Size, MaxSize(scaleset)) };

        // # Treat Azure knowledge of the size of the scaleset as "ground truth"
        var vmssSize = await _context.VmssOperations.GetVmssSize(scaleset.ScalesetId);
        if (vmssSize is null) {
            _log.Info($"scaleset is unavailable {scaleset.ScalesetId:Tag:ScalesetId}");

            //#if the scaleset is missing, this is an indication the scaleset
            //# was manually deleted, rather than having OneFuzz delete it.  As
            //# such, we should go thruogh the process of deleting it.
            return await SetShutdown(scaleset, now: true);
        } else if (scaleset.Size == vmssSize) {
            return await ResizeEqual(scaleset);
        } else if (scaleset.Size > vmssSize) {
            return await ResizeGrow(scaleset);
        } else {
            return await ResizeShrink(scaleset, vmssSize - scaleset.Size);
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

        _log.WithTag("Pool", scaleset.PoolName.ToString()).Event($"SetState Scaleset {scaleset.ScalesetId:Tag:ScalesetId} {scaleset.State:Tag:From} - {state:Tag:To}");
        var updatedScaleSet = scaleset with { State = state };
        var r = await Replace(updatedScaleSet);
        if (!r.IsOk) {
            _log.WithHttpStatus(r.ErrorV).Error($"Failed to update scaleset {updatedScaleSet.ScalesetId:Tag:ScalesetId} when updating {updatedScaleSet.State:Tag:StateFrom} - {state:Tag:StateTo}");
            // TODO: this should really return OneFuzzResult but then that propagates up the call stack
            throw new Exception($"Failed to update scaleset {updatedScaleSet.ScalesetId} when updating state from {updatedScaleSet.State} to {state}");
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

        return updatedScaleSet;
    }

    async Async.Task<Scaleset> SetFailed(Scaleset scaleset, Error error) {
        if (scaleset.Error is not null) {
            // already has an error, don't overwrite it
            return scaleset;
        }

        var updatedScaleset = await SetState(scaleset with { Error = error }, ScalesetState.CreationFailed);

        await _context.Events.SendEvent(new EventScalesetFailed(updatedScaleset.ScalesetId, updatedScaleset.PoolName, error));
        return updatedScaleset;
    }

    public async Async.Task<Scaleset> UpdateConfigs(Scaleset scaleSet) {
        if (scaleSet.State == ScalesetState.Halt) {
            _log.Info($"not updating configs, scalest is set to be deleted {scaleSet.ScalesetId:Tag:ScalesetId}");
            return scaleSet;
        }

        if (!scaleSet.NeedsConfigUpdate) {
            _log.Verbose($"config update no needed {scaleSet.ScalesetId:Tag:ScalesetId}");
            return scaleSet;
        }

        _log.Info($"updating scalset configs {scaleSet.ScalesetId:Tag:ScalesetId}");

        var pool = await _context.PoolOperations.GetByName(scaleSet.PoolName);
        if (!pool.IsOk) {
            _log.Error($"unable to find pool during config update {scaleSet.PoolName:Tag:PoolName} - {scaleSet.ScalesetId:Tag:ScalesetId}");
            scaleSet = await SetFailed(scaleSet, pool.ErrorV);
            return scaleSet;
        }

        var extensions = await _context.Extensions.FuzzExtensions(pool.OkV, scaleSet);
        var res = await _context.VmssOperations.UpdateExtensions(scaleSet.ScalesetId, extensions);
        if (!res.IsOk) {
            _log.Info($"unable to update configs {string.Join(',', res.ErrorV.Errors!)}");
            return scaleSet;
        }

        // successfully performed config update, save that fact:
        scaleSet = scaleSet with { NeedsConfigUpdate = false };
        var updateResult = await Update(scaleSet);
        if (!updateResult.IsOk) {
            _log.Info($"unable to set NeedsConfigUpdate to false - will try again");
        }

        return scaleSet;
    }

    public Async.Task<Scaleset> SetShutdown(Scaleset scaleset, bool now)
        => SetState(scaleset, now ? ScalesetState.Halt : ScalesetState.Shutdown);

    public async Async.Task<Scaleset> Setup(Scaleset scaleset) {
        //# TODO: How do we pass in SSH configs for Windows?  Previously
        //# This was done as part of the generated per-task setup script.
        _logTracer.Info($"setup {scaleset.ScalesetId:Tag:ScalesetId}");

        var network = await Network.Init(scaleset.Region, _context);
        var networkId = await network.GetId();
        if (networkId is null) {
            _logTracer.Info($"creating network {scaleset.Region:Tag:Region} - {scaleset.ScalesetId:Tag:ScalesetId}");
            var result = await network.Create();
            if (!result.IsOk) {
                return await SetFailed(scaleset, result.ErrorV);
            }

            //TODO : why are we saving scaleset here ? 
            var r = await Update(scaleset);
            if (!r.IsOk) {
                _logTracer.Error($"Failed to save scaleset {scaleset.ScalesetId:Tag:ScalesetId} due to {r.ErrorV:Tag:Error}");
            }

            return scaleset;
        }

        if (scaleset.Auth is null) {
            _logTracer.Error($"Scaleset Auth is missing for scaleset {scaleset.ScalesetId:Tag:ScalesetId}");
            return await SetFailed(scaleset, new Error(ErrorCode.UNABLE_TO_CREATE, new[] { "missing required auth" }));
        }

        var vmss = await _context.VmssOperations.GetVmss(scaleset.ScalesetId);

        if (vmss is null) {
            var pool = await _context.PoolOperations.GetByName(scaleset.PoolName);
            if (!pool.IsOk) {
                _logTracer.Error($"failed to get pool by name {scaleset.PoolName:Tag:PoolName} for scaleset: {scaleset.ScalesetId:Tag:ScalesetId}");
                return await SetFailed(scaleset, pool.ErrorV);
            }

            _logTracer.Info($"creating scaleset {scaleset.ScalesetId:Tag:ScalesetId}");
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
                _logTracer.Error($"Failed to create scaleset {scaleset.ScalesetId:Tag:ScalesetId} due to {result.ErrorV:Tag:Error}");
                return await SetFailed(scaleset, result.ErrorV);
            } else {
                // TODO: Link up auto scale resource with diagnostics
                _logTracer.Info($"creating scaleset: {scaleset.ScalesetId:Tag:ScalesetId}");
            }
        } else if (vmss.ProvisioningState == "Creating") {
            var result = TrySetIdentity(scaleset, vmss);
            if (!result.IsOk) {
                _logTracer.Warning($"Could not set identity due to: {result.ErrorV:Tag:Error} for {scaleset.ScalesetId:Tag:ScalesetId}");
            } else {
                scaleset = result.OkV;
            }
        } else {
            _logTracer.Info($"scaleset {scaleset.ScalesetId:Tag:ScalesetId} is in {vmss.ProvisioningState:Tag:ScalesetState}");

            var autoScaling = await TryEnableAutoScaling(scaleset);

            if (!autoScaling.IsOk) {
                _logTracer.Error($"failed to set auto-scaling for {scaleset.ScalesetId:Tag:ScalesetId} due to {autoScaling.ErrorV:Tag:Error}");
                return await SetFailed(scaleset, autoScaling.ErrorV);
            }

            var result = TrySetIdentity(scaleset, vmss);
            if (!result.IsOk) {
                _logTracer.Error($"failed to set identity for scaleset {scaleset.ScalesetId:Tag:ScalesetId} due to: {result.ErrorV:Tag:Error}");
                return await SetFailed(scaleset, result.ErrorV);
            } else {
                scaleset = await SetState(scaleset, ScalesetState.Running);
            }
        }

        var rr = await Replace(scaleset);
        if (!rr.IsOk) {
            _logTracer.WithHttpStatus(rr.ErrorV).Error($"Failed to save scale data for scale set: {scaleset.ScalesetId:Tag:ScalesetId}");
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
        _logTracer.Info($"Trying to add auto scaling for scaleset {scaleset.ScalesetId:Tag:ScalesetId}");

        var r = await _context.PoolOperations.GetByName(scaleset.PoolName);
        if (!r.IsOk) {
            _logTracer.Error($"Failed to get pool by name: {scaleset.PoolName:Tag:PoolName} - {r.ErrorV:Tag:Error}");
            return r.ErrorV;
        }
        var pool = r.OkV;
        var poolQueueId = _context.PoolOperations.GetPoolQueue(pool.PoolId);
        var poolQueueUri = _context.Queue.GetResourceId(poolQueueId, StorageType.Corpus);

        var capacity = await _context.VmssOperations.GetVmssSize(scaleset.ScalesetId);

        if (!capacity.HasValue) {
            var capacityFailed = OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_FIND, $"Failed to get capacity for scaleset {scaleset.ScalesetId}");
            _logTracer.Error($"Failed to get capacity for scaleset {scaleset.ScalesetId:Tag:ScalesetId}");
            return capacityFailed;
        }

        var autoScaleConfig = await _context.AutoScaleOperations.GetSettingsForScaleset(scaleset.ScalesetId);

        if (poolQueueUri is null) {
            var failedToFindQueueUri = OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_FIND, $"Failed to get pool queue uri for scaleset {scaleset.ScalesetId}");
            _logTracer.Error($"Failed to get pool queue uri for scaleset {scaleset.ScalesetId:Tag:ScalesetId}");
            return failedToFindQueueUri;
        }

        AutoscaleProfile autoScaleProfile;
        if (autoScaleConfig is null) {
            autoScaleProfile = _context.AutoScaleOperations.DefaultAutoScaleProfile(poolQueueUri!, capacity.Value);
        } else {
            _logTracer.Info($"Using existing auto scale settings from database for scaleset {scaleset.ScalesetId:Tag:ScalesetId}");
            autoScaleProfile = _context.AutoScaleOperations.CreateAutoScaleProfile(
                    queueUri: poolQueueUri!,
                    minAmount: autoScaleConfig.Min,
                    maxAmount: autoScaleConfig.Max,
                    defaultAmount: autoScaleConfig.Default,
                    scaleOutAmount: autoScaleConfig.ScaleOutAmount,
                    scaleOutCooldownMinutes: autoScaleConfig.ScaleOutCooldown,
                    scaleInAmount: autoScaleConfig.ScaleInAmount,
                    scaleInCooldownMinutes: autoScaleConfig.ScaleInCooldown
                );

        }

        _logTracer.Info($"Added auto scale resource to scaleset: {scaleset.ScalesetId:Tag:ScalesetId}");
        return await _context.AutoScaleOperations.AddAutoScaleToVmss(scaleset.ScalesetId, autoScaleProfile);
    }


    public async Async.Task<Scaleset> Init(Scaleset scaleset) {
        _logTracer.Info($"init {scaleset.ScalesetId:Tag:ScalesetId}");
        var shrinkQueue = new ShrinkQueue(scaleset.ScalesetId, _context.Queue, _logTracer);
        await shrinkQueue.Create();
        // Handle the race condition between a pool being deleted and a
        // scaleset being added to the pool.

        var poolResult = await _context.PoolOperations.GetByName(scaleset.PoolName);
        if (!poolResult.IsOk) {
            _logTracer.Error($"failed to get pool by name {scaleset.PoolName:Tag:PoolName} for scaleset: {scaleset.ScalesetId:Tag:ScalesetId} due to {poolResult.ErrorV:Tag:Error}");
            return await SetFailed(scaleset, poolResult.ErrorV);
        }

        var pool = poolResult.OkV;

        if (pool.State == PoolState.Init) {
            _logTracer.Info($"waiting for pool {scaleset.PoolName:Tag:PoolName} - {scaleset.ScalesetId:Tag:ScalesetId}");
        } else if (pool.State == PoolState.Running) {
            var imageOsResult = await _context.ImageOperations.GetOs(scaleset.Region, scaleset.Image);
            if (!imageOsResult.IsOk) {
                _logTracer.Error($"failed to get OS with region: {scaleset.Region:Tag:Region} {scaleset.Image:Tag:Image} for scaleset: {scaleset.ScalesetId:Tag:ScalesetId} due to {imageOsResult.ErrorV:Tag:Error}");
                return await SetFailed(scaleset, imageOsResult.ErrorV);
            } else if (imageOsResult.OkV != pool.Os) {
                _logTracer.Error($"got invalid OS: {imageOsResult.OkV:Tag:ActualOs} for scaleset: {scaleset.ScalesetId:Tag:ScalesetId} expected OS {pool.Os:Tag:ExpectedOs}");
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
            _log.Info($"deleting node {scaleset.ScalesetId:Tag:ScalesetId} - {node.MachineId:Tag:MachineId}");
            await _context.NodeOperations.Delete(node);
        }
        _log.Info($"scaleset delete starting - {scaleset.ScalesetId:Tag:ScalesetId}");

        if (await _context.VmssOperations.DeleteVmss(scaleset.ScalesetId)) {
            _log.Info($"scaleset deleted: {scaleset.ScalesetId:Tag:ScalesetId}");
            var r = await Delete(scaleset);
            if (!r.IsOk) {
                _log.WithHttpStatus(r.ErrorV).Error($"Failed to delete scaleset record {scaleset.ScalesetId:Tag:ScalesetId}");
            }
        } else {
            var r = await Update(scaleset);
            if (!r.IsOk) {
                _log.WithHttpStatus(r.ErrorV).Error($"Failed to save scaleset record {scaleset.ScalesetId:Tag:ScalesetId}");
            }
        }

        return scaleset;
    }

    /// <summary>
    /// Cleanup scaleset nodes
    /// </summary>
    /// <param name="scaleSet"></param>
    /// <returns>true if scaleset got modified</returns>
    public async Async.Task<(bool, Scaleset)> CleanupNodes(Scaleset scaleSet) {
        _log.Info($"cleaning up nodes {scaleSet.ScalesetId:Tag:ScalesetId}");

        if (scaleSet.State == ScalesetState.Halt) {
            _log.Info($"halting scaleset {scaleSet.ScalesetId:Tag:ScalesetId}");
            scaleSet = await Halt(scaleSet);
            return (true, scaleSet);
        }

        var pool = await _context.PoolOperations.GetByName(scaleSet.PoolName);
        if (!pool.IsOk) {
            _log.Error($"unable to find pool during cleanup {scaleSet.ScalesetId:Tag:ScalesetId} - {scaleSet.PoolName:Tag:PoolName}");
            scaleSet = await SetFailed(scaleSet, pool.ErrorV!);
            return (true, scaleSet);
        }

        await _context.NodeOperations.ReimageLongLivedNodes(scaleSet.ScalesetId);

        //ground truth of existing nodes
        var azureNodes = await _context.VmssOperations.ListInstanceIds(scaleSet.ScalesetId);
        var nodes = _context.NodeOperations.SearchStates(scalesetId: scaleSet.ScalesetId);

        //# Nodes do not exists in scalesets but in table due to unknown failure
        await foreach (var node in nodes) {
            if (!azureNodes.ContainsKey(node.MachineId)) {
                _log.Info($"{node.MachineId:Tag:MachineId} no longer in scaleset {scaleSet.ScalesetId:Tag:ScalesetId}");
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

            _log.Info($"adding missing azure node {machineId:Tag:MachineId} {scaleSet.ScalesetId:Tag:ScalesetId}");

            // Note, using isNew:True makes it such that if a node already has
            // checked in, this won't overwrite it.

            // don't use result, if there is one
            _ = await _context.NodeOperations.Create(
                pool.OkV.PoolId,
                scaleSet.PoolName,
                machineId,
                scaleSet.ScalesetId,
                _context.ServiceConfiguration.OneFuzzVersion,
                isNew: true);
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
                    toDelete[node.MachineId] = await _context.NodeOperations.SetHalt(node);
                } else if (await new ShrinkQueue(pool.OkV!.PoolId, _context.Queue, _log).ShouldShrink()) {
                    toDelete[node.MachineId] = await _context.NodeOperations.SetHalt(node);
                } else {
                    _logTracer.Info($"Node ready to reimage {node.MachineId:Tag:MachineId} {node.ScalesetId:Tag:ScalesetId} {node.State:Tag:State}");
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

            _log.Info($"{errorMessage} {deadNode.MachineId:Tag:MachineId} {deadNode.ScalesetId:Tag:ScalesetId}");

            var error = new Error(ErrorCode.TASK_FAILED, new[] { $"{errorMessage} scaleset_id {deadNode.ScalesetId} last heartbeat:{deadNode.Heartbeat}" });
            await _context.NodeOperations.MarkTasksStoppedEarly(deadNode, error);
            toReimage[deadNode.MachineId] = await _context.NodeOperations.ToReimage(deadNode, true);
        }

        // Perform operations until they fail due to scaleset getting locked:
        var strategy = _context.ServiceConfiguration.OneFuzzNodeDisposalStrategy.ToLowerInvariant() switch {
            // allowing typo’d or correct name for config setting:
            "decomission" or "decommission" => NodeDisposalStrategy.Decommission,
            _ => NodeDisposalStrategy.ScaleIn,
        };

        await ReimageNodes(scaleSet, toReimage.Values, strategy);
        await DeleteNodes(scaleSet, toDelete.Values, strategy);

        return (toReimage.Count > 0 || toDelete.Count > 0, scaleSet);
    }


    public async Async.Task ReimageNodes(Scaleset scaleset, IEnumerable<Node> nodes, NodeDisposalStrategy disposalStrategy) {

        if (nodes is null || !nodes.Any()) {
            _log.Info($"no nodes to reimage: {scaleset.ScalesetId:Tag:ScalesetId}");
            return;
        }

        if (scaleset.State == ScalesetState.Shutdown) {
            _log.Info($"scaleset shutting down, deleting rather than reimaging nodes {scaleset.ScalesetId:Tag:ScalesetId}");
            await DeleteNodes(scaleset, nodes, disposalStrategy);
            return;
        }

        if (scaleset.State == ScalesetState.Halt) {
            _log.Info($"scaleset halting, ignoring node reimage {scaleset.ScalesetId:Tag:ScalesetId}");
            return;
        }

        var machineIds = new HashSet<Guid>();
        foreach (var node in nodes) {
            if (node.State != NodeState.Done) {
                continue;
            }

            if (node.DebugKeepNode) {
                _log.Warning($"not reimaging manually overriden node {node.MachineId:Tag:MachineId} in scaleset {scaleset.ScalesetId:Tag:ScalesetId}");
            } else {
                _ = machineIds.Add(node.MachineId);
            }
        }

        if (!machineIds.Any()) {
            _log.Info($"no nodes to reimage {scaleset.ScalesetId:Tag:ScalesetId}");
            return;
        }

        switch (disposalStrategy) {
            case NodeDisposalStrategy.Decommission:
                _log.Info($"decommissioning nodes");
                await Async.Task.WhenAll(nodes
                    .Where(node => machineIds.Contains(node.MachineId))
                    .Select(async node => {
                        await _context.NodeOperations.ReleaseScaleInProtection(node).IgnoreResult();
                    }));
                return;

            case NodeDisposalStrategy.ScaleIn:
                var r = await _context.VmssOperations.ReimageNodes(scaleset.ScalesetId, machineIds);
                if (r.IsOk) {
                    await Async.Task.WhenAll(nodes
                        .Where(node => machineIds.Contains(node.MachineId))
                        .Select(async node => {
                            var r = await _context.NodeOperations.ReleaseScaleInProtection(node);
                            if (r.IsOk) {
                                await _context.NodeOperations.Delete(node);
                            }
                        }));
                } else {
                    _log.Info($"failed to reimage nodes due to {r.ErrorV:Tag:Error}");
                }
                return;
        }
    }


    public async Async.Task DeleteNodes(Scaleset scaleset, IEnumerable<Node> nodes, NodeDisposalStrategy disposalStrategy) {
        if (nodes is null || !nodes.Any()) {
            _log.Info($"no nodes to delete: scaleset_id: {scaleset.ScalesetId:Tag:ScalesetId}");
            return;
        }

        // TODO: try to do this as one atomic operation:
        nodes = await Async.Task.WhenAll(nodes.Select(node => _context.NodeOperations.SetHalt(node)));

        if (scaleset.State == ScalesetState.Halt) {
            _log.Info($"scaleset halting, ignoring deletion {scaleset.ScalesetId:Tag:ScalesetId}");
            return;
        }

        HashSet<Guid> machineIds = new();
        foreach (var node in nodes) {
            if (node.DebugKeepNode) {
                _log.Warning($"not deleting manually overriden node {node.MachineId:Tag:MachineId} in scaleset {scaleset.ScalesetId:Tag:ScalesetId}");
            } else {
                _ = machineIds.Add(node.MachineId);
            }
        }

        switch (disposalStrategy) {
            case NodeDisposalStrategy.Decommission:
                _log.Info($"decommissioning nodes");
                await Async.Task.WhenAll(nodes
                    .Where(node => machineIds.Contains(node.MachineId))
                    .Select(async node => {
                        await _context.NodeOperations.ReleaseScaleInProtection(node).IgnoreResult();
                    }));
                return;

            case NodeDisposalStrategy.ScaleIn:
                _log.Info($"deleting nodes {scaleset.ScalesetId:Tag:ScalesetId} {string.Join(", ", machineIds):Tag:MachineIds}");
                await _context.VmssOperations.DeleteNodes(scaleset.ScalesetId, machineIds);
                await Async.Task.WhenAll(nodes
                    .Where(node => machineIds.Contains(node.MachineId))
                    .Select(async node => {
                        await _context.NodeOperations.Delete(node);
                        await _context.NodeOperations.ReleaseScaleInProtection(node).IgnoreResult();
                    }));
                return;
        }
    }

    public async Task<OneFuzzResult<Scaleset>> GetById(Guid scalesetId) {
        var data = QueryAsync(filter: Query.RowKey(scalesetId.ToString()));
        var scaleSets = data is not null ? (await data.ToListAsync()) : null;

        if (scaleSets == null || scaleSets.Count == 0) {
            return OneFuzzResult<Scaleset>.Error(
                ErrorCode.INVALID_REQUEST,
                "unable to find scaleset"
            );
        }

        if (scaleSets.Count != 1) {
            return OneFuzzResult<Scaleset>.Error(
                ErrorCode.INVALID_REQUEST,
                "error identifying scaleset"
            );
        }

        return OneFuzzResult<Scaleset>.Ok(scaleSets.First());
    }

    public IAsyncEnumerable<Scaleset> GetByObjectId(Guid objectId) {
        return QueryAsync(filter: $"client_object_id eq '{objectId}'");
    }


    private async Async.Task<Scaleset> ResizeEqual(Scaleset scaleset) {
        //# NOTE: this is the only place we reset to the 'running' state.
        //# This ensures that our idea of scaleset size agrees with Azure

        var nodeCount = await _context.NodeOperations.SearchStates(scalesetId: scaleset.ScalesetId).CountAsync();
        if (nodeCount == scaleset.Size) {
            _log.Info($"resize finished {scaleset.ScalesetId:Tag:ScalesetId}");
            return await SetState(scaleset, ScalesetState.Running);
        } else {
            _log.Info($"resize finished, waiting for nodes to check in {scaleset.ScalesetId:Tag:ScalesetId} ({nodeCount:Tag:NodeCount} of {scaleset.Size:Tag:Size} checked in)");
            return scaleset;
        }
    }

    private async Async.Task<Scaleset> ResizeGrow(Scaleset scaleset) {
        var resizeResult = await _context.VmssOperations.ResizeVmss(scaleset.ScalesetId, scaleset.Size);
        if (resizeResult.IsOk == false) {
            _log.Info($"scaleset is mid-operation already {scaleset.ScalesetId:Tag:ScalesetId} {resizeResult.ErrorV:Tag:Error}");
        }
        return scaleset;
    }

    private async Async.Task<Scaleset> ResizeShrink(Scaleset scaleset, long? toRemove) {
        _log.Info($"shrinking scaleset {scaleset.ScalesetId:Tag:ScalesetId} {toRemove:Tag:ToRemove}");

        if (!toRemove.HasValue) {
            return scaleset;
        } else {
            var queue = new ShrinkQueue(scaleset.ScalesetId, _context.Queue, _log);
            await queue.SetSize(toRemove.Value);
            var nodes = _context.NodeOperations.SearchStates(scalesetId: scaleset.ScalesetId);
            await foreach (var node in nodes) {
                await _context.NodeOperations.SendStopIfFree(node);
            }
            return scaleset;
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
            _logTracer.Info($"scale set shutdown: scaleset already deleted {scaleset.ScalesetId:Tag:ScalesetId}");
            return await Halt(scaleset);
        }

        _logTracer.Info($"scaleset shutdown {scaleset.ScalesetId:Tag:ScalesetId} {size:Tag:Size}");
        {
            var nodes = _context.NodeOperations.SearchStates(scalesetId: scaleset.ScalesetId);
            // TODO: Parallelization opportunity
            await nodes.ForEachAwaitAsync(_context.NodeOperations.SetShutdown);
        }

        _logTracer.Info($"checking for existing auto scale settings {scaleset.ScalesetId:Tag:ScalesetId}");

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
                _logTracer.Info($"Getting nodes with scale in protection");

                var vmsWithProtection = await _context.VmssOperations.ListVmss(
                    scaleset.ScalesetId,
                    (vmResource) => vmResource?.Data?.ProtectionPolicy?.ProtectFromScaleIn != null
                        && vmResource.Data.ProtectionPolicy.ProtectFromScaleIn.Value
                );

                _logTracer.Info($"{JsonSerializer.Serialize(vmsWithProtection):Tag:VMsWithProtection}");
                if (vmsWithProtection != null) {
                    var numVmsWithProtection = vmsWithProtection.Count;
                    profile.Capacity.Minimum = numVmsWithProtection.ToString();
                    profile.Capacity.Default = numVmsWithProtection.ToString();
                } else {
                    _logTracer.Error($"Failed to list vmss for scaleset {scaleset.ScalesetId:Tag:ScalesetId}");
                }
            }

            var updatedAutoScale = await _context.AutoScaleOperations.UpdateAutoscale(autoScalePolicy.OkV.Data);
            if (!updatedAutoScale.IsOk) {
                _logTracer.Error($"Failed to update auto scale {updatedAutoScale.ErrorV:Tag:Error}");
            }
        } else if (!autoScalePolicy.IsOk) {
            _logTracer.Error(autoScalePolicy.ErrorV);
        } else {
            _logTracer.Info($"No existing auto scale settings found for {scaleset.ScalesetId:Tag:ScalesetId}");
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

    public Task<Scaleset> Running(Scaleset scaleset) {
        // nothing to do
        return Async.Task.FromResult(scaleset);
    }

    public Task<Scaleset> CreationFailed(Scaleset scaleset) {
        // nothing to do
        return Async.Task.FromResult(scaleset);
    }
}
