using System.Text.Json;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Monitor;
using Azure.ResourceManager.Monitor.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;

public interface IScalesetOperations : IStatefulOrm<Scaleset, ScalesetState> {
    IAsyncEnumerable<Scaleset> Search();

    IAsyncEnumerable<Scaleset> SearchByPool(PoolName poolName);

    Async.Task<Scaleset> UpdateConfigs(Scaleset scaleSet);

    Async.Task<OneFuzzResult<Scaleset>> GetById(ScalesetId scalesetId);
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
    private readonly IMemoryCache _cache;

    public ScalesetOperations(ILogger<ScalesetOperations> log, IMemoryCache cache, IOnefuzzContext context)
        : base(log, context) {
        _logTracer.AddTag("Component", "scalesets");
        _cache = cache;
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
            _logTracer.LogInformation("scaleset is unavailable {ScalesetId}", scaleset.ScalesetId);
            //#if the scaleset is missing, this is an indication the scaleset
            //# was manually deleted, rather than having OneFuzz delete it.  As
            //# such, we should go thruogh the process of deleting it.
            scaleset = await SetShutdown(scaleset, now: true);
            return scaleset;
        }

        if (size != scaleset.Size) {
            //# Azure auto-scaled us or nodes were manually added/removed
            //# New node state will be synced in cleanup_nodes
            _logTracer.LogInformation("unexpected scaleset size, resizing {ScalesetId} {ExpectedSize} {ActualSize}", scaleset.ScalesetId, scaleset.Size, size);

            scaleset = scaleset with { Size = size.Value };
            var replaceResult = await Replace(scaleset);
            if (!replaceResult.IsOk) {
                _logTracer.AddHttpStatus(replaceResult.ErrorV);
                _logTracer.LogError("failed to update scaleset size for {ScalesetId}", scaleset.ScalesetId);
            }
        }

        return scaleset;
    }

    public async Async.Task<OneFuzzResultVoid> SyncAutoscaleSettings(Scaleset scaleset) {
        if (scaleset.State != ScalesetState.Running)
            return OneFuzzResultVoid.Ok;

        _logTracer.LogInformation("syncing auto-scale settings for scaleset {ScalesetId}", scaleset.ScalesetId);

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
                _logTracer.LogInformation("Scaleout cooldown in seconds. {Before}", scaleOutCooldown);
                scaleOutCooldown = (long)scaleAction.Cooldown.TotalMinutes;
                _logTracer.LogInformation("Scaleout cooldown in seconds. {After}", scaleOutCooldown);
            } else if (scaleAction.Direction == ScaleDirection.Decrease) {
                scaleInAmount = Int32.Parse(scaleAction.Value);
                _logTracer.LogInformation("Scalin cooldown in seconds. {Before}", scaleInCooldown);
                scaleInCooldown = (long)scaleAction.Cooldown.TotalMinutes;
                _logTracer.LogInformation("Scalein cooldown in seconds. {After}", scaleInCooldown);
            } else {
                continue;
            }
        }

        var poolResult = await _context.PoolOperations.GetByName(scaleset.PoolName);
        if (!poolResult.IsOk) {
            return poolResult.ErrorV;
        }

        _logTracer.LogInformation("Updating auto-scale entry for scaleset: {ScalesetId}", scaleset.ScalesetId);
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

        _logTracer.LogInformation("scaleset resize: {ScalesetId} - {Size}", scaleset.ScalesetId, scaleset.Size);

        var shrinkQueue = new ShrinkQueue(scaleset.ScalesetId, _context.Queue, _logTracer);
        // # reset the node delete queue
        await shrinkQueue.Clear();

        //# just in case, always ensure size is within max capacity
        scaleset = scaleset with { Size = Math.Min(scaleset.Size, scaleset.Image.MaximumVmCount) };

        // # Treat Azure knowledge of the size of the scaleset as "ground truth"
        var vmssSize = await _context.VmssOperations.GetVmssSize(scaleset.ScalesetId);
        if (vmssSize is null) {
            _logTracer.LogInformation("scaleset is unavailable {ScalesetId}", scaleset.ScalesetId);

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

        _logTracer.AddTags(new Dictionary<string, string>() {
            { "ScalesetId", scaleset.ScalesetId.ToString()},
            { "From", scaleset.State.ToString()},
            { "To", state.ToString()}
        });

        _logTracer.AddTag("Pool", scaleset.PoolName.ToString());
        _logTracer.LogEvent("SetState Scaleset");
        var updatedScaleSet = scaleset with { State = state };
        var r = await Replace(updatedScaleSet);
        if (!r.IsOk) {
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("Failed to update scaleset when updating state");
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
            _logTracer.LogInformation("not updating configs, scalest is set to be deleted {ScalesetId}", scaleSet.ScalesetId);
            return scaleSet;
        }

        if (!scaleSet.NeedsConfigUpdate) {
            _logTracer.LogDebug("config update no needed {ScalesetId}", scaleSet.ScalesetId);
            return scaleSet;
        }

        _logTracer.LogInformation("updating scalset configs {ScalesetId}", scaleSet.ScalesetId);

        var pool = await _context.PoolOperations.GetByName(scaleSet.PoolName);
        if (!pool.IsOk) {
            _logTracer.LogError("unable to find pool during config update {PoolName} - {ScalesetId}", scaleSet.PoolName, scaleSet.ScalesetId);
            scaleSet = await SetFailed(scaleSet, pool.ErrorV);
            return scaleSet;
        }

        var extensions = await _context.Extensions.FuzzExtensions(pool.OkV, scaleSet);
        var res = await _context.VmssOperations.UpdateExtensions(scaleSet.ScalesetId, extensions);
        if (!res.IsOk) {
            _logTracer.LogInformation("unable to update configs {erorrs}", string.Join(',', res.ErrorV.Errors!));
            return scaleSet;
        }

        // successfully performed config update, save that fact:
        scaleSet = scaleSet with { NeedsConfigUpdate = false };
        var updateResult = await Update(scaleSet);
        if (!updateResult.IsOk) {
            _logTracer.LogInformation("unable to set NeedsConfigUpdate to false - will try again");
        }

        return scaleSet;
    }

    public Async.Task<Scaleset> SetShutdown(Scaleset scaleset, bool now)
        => SetState(scaleset, now ? ScalesetState.Halt : ScalesetState.Shutdown);

    public async Async.Task<Scaleset> Setup(Scaleset scaleset) {
        //# TODO: How do we pass in SSH configs for Windows?  Previously
        //# This was done as part of the generated per-task setup script.
        _logTracer.LogInformation("setup {ScalesetId}", scaleset.ScalesetId);

        var network = await Network.Init(scaleset.Region, _context);
        var networkId = await network.GetId();
        if (networkId is null) {
            _logTracer.LogInformation("creating network {Region} - {ScalesetId}", scaleset.Region, scaleset.ScalesetId);
            var result = await network.Create();
            if (!result.IsOk) {
                return await SetFailed(scaleset, result.ErrorV);
            }

            //TODO : why are we saving scaleset here ?
            var r = await Update(scaleset);
            if (!r.IsOk) {
                _logTracer.LogError("Failed to save scaleset {ScalesetId} due to {Error}", scaleset.ScalesetId, r.ErrorV);
            }

            return scaleset;
        }

        if (scaleset.Auth is null) {
            _logTracer.LogError("Scaleset Auth is missing for scaleset {ScalesetId}", scaleset.ScalesetId);
            return await SetFailed(scaleset, Error.Create(ErrorCode.UNABLE_TO_CREATE, "missing required auth"));
        }
        var auth = await _context.SecretsOperations.GetSecretValue(scaleset.Auth);

        if (auth is null) {
            _logTracer.LogError("Scaleset Auth is missing for scaleset {ScalesetId}", scaleset.ScalesetId);
            return await SetFailed(scaleset, Error.Create(ErrorCode.UNABLE_TO_CREATE, "missing required auth"));
        }


        var vmss = await _context.VmssOperations.GetVmss(scaleset.ScalesetId);

        if (vmss is null) {
            var pool = await _context.PoolOperations.GetByName(scaleset.PoolName);
            if (!pool.IsOk) {
                _logTracer.LogError("failed to get pool by name {PoolName} for scaleset: {ScalesetId}", scaleset.PoolName, scaleset.ScalesetId);
                return await SetFailed(scaleset, pool.ErrorV);
            }

            _logTracer.LogInformation("creating scaleset {ScalesetId}", scaleset.ScalesetId);
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
                            auth.Password,
                            auth.PublicKey,
                            scaleset.Tags);

            if (!result.IsOk) {
                _logTracer.LogError("Failed to create scaleset {ScalesetId} due to {Error}", scaleset.ScalesetId, result.ErrorV);
                return await SetFailed(scaleset, result.ErrorV);
            } else {
                // TODO: Link up auto scale resource with diagnostics
                _logTracer.LogInformation("creating scaleset: {ScalesetId}", scaleset.ScalesetId);
            }
        } else if (vmss.ProvisioningState == "Creating") {
            var result = TrySetIdentity(scaleset, vmss);
            if (!result.IsOk) {
                _logTracer.LogWarning("Could not set identity due to: {Error} for {ScalesetId}", result.ErrorV, scaleset.ScalesetId);
            } else {
                scaleset = result.OkV;
            }
        } else {
            _logTracer.LogInformation("scaleset {ScalesetId} is in {ScalesetState}", scaleset.ScalesetId, vmss.ProvisioningState);

            var autoScaling = await TryEnableAutoScaling(scaleset);

            if (!autoScaling.IsOk) {
                _logTracer.LogError("failed to set auto-scaling for {ScalesetId} due to {Error}", scaleset.ScalesetId, autoScaling.ErrorV);
                return await SetFailed(scaleset, autoScaling.ErrorV);
            }

            var result = TrySetIdentity(scaleset, vmss);
            if (!result.IsOk) {
                _logTracer.LogError("failed to set identity for scaleset {ScalesetId} due to: {Error}", scaleset.ScalesetId, result.ErrorV);
                return await SetFailed(scaleset, result.ErrorV);
            } else {
                scaleset = await SetState(scaleset, ScalesetState.Running);
            }
        }

        var rr = await Replace(scaleset);
        if (!rr.IsOk) {
            _logTracer.AddHttpStatus(rr.ErrorV);
            _logTracer.LogError("Failed to save scale data for scale set: {ScalesetId}", scaleset.ScalesetId);
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
        _logTracer.LogInformation("Trying to add auto scaling for scaleset {ScalesetId}", scaleset.ScalesetId);

        var r = await _context.PoolOperations.GetByName(scaleset.PoolName);
        if (!r.IsOk) {
            _logTracer.LogError("Failed to get pool by name: {PoolName} - {Error}", scaleset.PoolName, r.ErrorV);
            return r.ErrorV;
        }
        var pool = r.OkV;
        var poolQueueId = _context.PoolOperations.GetPoolQueue(pool.PoolId);
        var poolQueueUri = _context.Queue.GetResourceId(poolQueueId, StorageType.Corpus);

        var capacity = await _context.VmssOperations.GetVmssSize(scaleset.ScalesetId);

        if (!capacity.HasValue) {
            var capacityFailed = OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_FIND, $"Failed to get capacity for scaleset {scaleset.ScalesetId}");
            _logTracer.LogError("Failed to get capacity for scaleset {ScalesetId}", scaleset.ScalesetId);
            return capacityFailed;
        }

        var autoScaleConfig = await _context.AutoScaleOperations.GetSettingsForScaleset(scaleset.ScalesetId);

        if (poolQueueUri is null) {
            var failedToFindQueueUri = OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_FIND, $"Failed to get pool queue uri for scaleset {scaleset.ScalesetId}");
            _logTracer.LogError("Failed to get pool queue uri for scaleset {ScalesetId}", scaleset.ScalesetId);
            return failedToFindQueueUri;
        }

        AutoscaleProfile autoScaleProfile;
        if (autoScaleConfig is null) {
            autoScaleProfile = _context.AutoScaleOperations.DefaultAutoScaleProfile(poolQueueUri!, capacity.Value);
        } else {
            _logTracer.LogInformation("Using existing auto scale settings from database for scaleset {ScalesetId}", scaleset.ScalesetId);
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

        _logTracer.LogInformation("Added auto scale resource to scaleset: {ScalesetId}", scaleset.ScalesetId);
        return await _context.AutoScaleOperations.AddAutoScaleToVmss(scaleset.ScalesetId, autoScaleProfile);
    }


    public async Async.Task<Scaleset> Init(Scaleset scaleset) {
        _logTracer.LogInformation("init {ScalesetId}", scaleset.ScalesetId);
        var shrinkQueue = new ShrinkQueue(scaleset.ScalesetId, _context.Queue, _logTracer);
        await shrinkQueue.Create();
        // Handle the race condition between a pool being deleted and a
        // scaleset being added to the pool.

        var poolResult = await _context.PoolOperations.GetByName(scaleset.PoolName);
        if (!poolResult.IsOk) {
            _logTracer.LogError("failed to get pool by name {PoolName} for scaleset: {ScalesetId} due to {Error}", scaleset.PoolName, scaleset.ScalesetId, poolResult.ErrorV);
            return await SetFailed(scaleset, poolResult.ErrorV);
        }

        var pool = poolResult.OkV;

        if (pool.State == PoolState.Init) {
            _logTracer.LogInformation("waiting for pool {PoolName} - {ScalesetId}", scaleset.PoolName, scaleset.ScalesetId);
        } else if (pool.State == PoolState.Running) {
            var imageOsResult = await scaleset.Image.GetOs(_cache, _context.Creds.ArmClient, scaleset.Region);
            if (!imageOsResult.IsOk) {
                _logTracer.LogError("failed to get OS with region: {Region} {Image} for scaleset: {ScalesetId} due to {Error}", scaleset.Region, scaleset.Image, scaleset.ScalesetId, imageOsResult.ErrorV);
                return await SetFailed(scaleset, imageOsResult.ErrorV);
            } else if (imageOsResult.OkV != pool.Os) {
                _logTracer.LogError("got invalid OS: {ActualOs} for scaleset: {ScalesetId} expected OS {ExpectedOs}", imageOsResult.OkV, scaleset.ScalesetId, pool.Os);
                return await SetFailed(scaleset, Error.Create(ErrorCode.INVALID_REQUEST, $"invalid os (got: {imageOsResult.OkV} needed: {pool.Os})"));
            } else {
                return await SetState(scaleset, ScalesetState.Setup);
            }
        } else {
            return await SetState(scaleset, ScalesetState.Setup);
        }

        return scaleset;
    }

    public async Async.Task<Scaleset> Halt(Scaleset scaleset) {
        var shrinkQueue = new ShrinkQueue(scaleset.ScalesetId, _context.Queue, _logTracer);
        await shrinkQueue.Delete();

        await foreach (var node in _context.NodeOperations.SearchStates(scalesetId: scaleset.ScalesetId)) {
            _logTracer.LogInformation("deleting node {ScalesetId} - {MachineId}", scaleset.ScalesetId, node.MachineId);
            await _context.NodeOperations.Delete(node, "scaleset is being shutdown");
        }
        _logTracer.LogInformation("scaleset delete starting - {ScalesetId}", scaleset.ScalesetId);

        if (await _context.VmssOperations.DeleteVmss(scaleset.ScalesetId)) {
            _logTracer.LogInformation("scaleset deleted: {ScalesetId}", scaleset.ScalesetId);
            var r = await Delete(scaleset);
            if (!r.IsOk) {
                _logTracer.AddHttpStatus(r.ErrorV);
                _logTracer.LogError("Failed to delete scaleset record {ScalesetId}", scaleset.ScalesetId);
            }
            var autoscaleEntry = __context.AutoScaleOperations.GetSettingsForScaleset(scaleset.ScalesetId);
            if (autoscaleEntry is null) {
                _logTracer.LogInformation("Could not find autoscale settings for scaleset {ScalesetId}", scaleset.ScalesetId);
            }
            else {
                _logTracer.LogInformation("Deleting autoscale entry for scaleset {ScalesetId}", scaleset.ScalesetId);
                autoscaleEntry.Delete()
            }
        } else {
            var r = await Update(scaleset);
            if (!r.IsOk) {
                _logTracer.AddHttpStatus(r.ErrorV);
                _logTracer.LogError("Failed to save scaleset record {ScalesetId}", scaleset.ScalesetId);
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
        _logTracer.LogInformation("cleaning up nodes {ScalesetId}", scaleSet.ScalesetId);

        if (scaleSet.State == ScalesetState.Halt) {
            _logTracer.LogInformation("halting scaleset {ScalesetId}", scaleSet.ScalesetId);
            scaleSet = await Halt(scaleSet);
            return (true, scaleSet);
        }

        var pool = await _context.PoolOperations.GetByName(scaleSet.PoolName);
        if (!pool.IsOk) {
            _logTracer.LogError("unable to find pool during cleanup {ScalesetId} - {PoolName}", scaleSet.ScalesetId, scaleSet.PoolName);
            scaleSet = await SetFailed(scaleSet, pool.ErrorV!);
            return (true, scaleSet);
        }

        await _context.NodeOperations.ReimageLongLivedNodes(scaleSet.ScalesetId);

        //ground truth of existing nodes
        var azureNodes = await _context.VmssOperations.ListInstanceIds(scaleSet.ScalesetId);
        if (!azureNodes.Any()) {
            // didn't find scaleset
            return (false, scaleSet);
        }

        var nodes = _context.NodeOperations.SearchStates(scalesetId: scaleSet.ScalesetId);

        //# Nodes do not exists in scalesets but in table due to unknown failure
        await foreach (var node in nodes) {
            if (!azureNodes.ContainsKey(node.MachineId)) {
                _logTracer.LogInformation("{MachineId} no longer in scaleset {ScalesetId}", node.MachineId, scaleSet.ScalesetId);
                await _context.NodeOperations.Delete(node, "node is being cleaned up because it is no longer in the scaleset");
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
            var instanceId = azureNode.Value;
            if (nodeMachineIds.Contains(machineId)) {
                continue;
            }

            _logTracer.LogInformation("adding missing azure node {MachineId} {ScalesetId}", machineId, scaleSet.ScalesetId);

            // Note, using isNew:True makes it such that if a node already has
            // checked in, this won't overwrite it.

            // don't use result, if there is one
            _ = await _context.NodeOperations.Create(
                pool.OkV.PoolId,
                scaleSet.PoolName,
                machineId,
                instanceId,
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
                if (await new ShrinkQueue(scaleSet.ScalesetId, _context.Queue, _logTracer).ShouldShrink()) {
                    toDelete[node.MachineId] = await _context.NodeOperations.SetHalt(node);
                } else if (await new ShrinkQueue(pool.OkV.PoolId, _context.Queue, _logTracer).ShouldShrink()) {
                    toDelete[node.MachineId] = await _context.NodeOperations.SetHalt(node);
                } else {
                    _logTracer.LogInformation("Node ready to reimage {MachineId} {ScalesetId} {State}", node.MachineId, node.ScalesetId, node.State);
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

            _logTracer.LogInformation("{errorMessage} {MachineId} {ScalesetId}", errorMessage, deadNode.MachineId, deadNode.ScalesetId);

            var error = Error.Create(ErrorCode.TASK_FAILED, $"{errorMessage} scaleset_id {deadNode.ScalesetId} last heartbeat:{deadNode.Heartbeat}");
            await _context.NodeOperations.MarkTasksStoppedEarly(deadNode, error);
            toReimage[deadNode.MachineId] = await _context.NodeOperations.ToReimage(deadNode, true);
        }

        // Perform operations until they fail due to scaleset getting locked:
        var strategy = await _context.FeatureManagerSnapshot.IsEnabledAsync(FeatureFlagConstants.EnableNodeDecommissionStrategy) ? NodeDisposalStrategy.Decommission : NodeDisposalStrategy.ScaleIn;

        var reimageNodes = await ReimageNodes(scaleSet, toReimage.Values, strategy);
        if (!reimageNodes.IsOk) {
            _logTracer.LogWarning("{error}", reimageNodes.ErrorV);
            return (false, scaleSet);
        }
        var deleteNodes = await DeleteNodes(scaleSet, toDelete.Values, "Node was ReadyForReset");
        if (!deleteNodes.IsOk) {
            _logTracer.LogWarning("{error}", deleteNodes.ErrorV);
            return (toReimage.Count > 0, scaleSet);
        }

        return (toReimage.Count > 0 || toDelete.Count > 0, scaleSet);
    }


    public async Async.Task<OneFuzzResultVoid> ReimageNodes(Scaleset scaleset, IEnumerable<Node> nodes, NodeDisposalStrategy disposalStrategy) {
        if (nodes is null || !nodes.Any()) {
            _logTracer.LogInformation("no nodes to reimage: {ScalesetId}", scaleset.ScalesetId);
            return OneFuzzResultVoid.Ok;
        }

        if (scaleset.State == ScalesetState.Shutdown) {
            _logTracer.LogInformation("scaleset shutting down, deleting rather than reimaging nodes {ScalesetId}", scaleset.ScalesetId);
            return await DeleteNodes(scaleset, nodes, "scaleset is shutting down");
        }

        if (scaleset.State == ScalesetState.Halt) {
            _logTracer.LogInformation("scaleset halting, ignoring node reimage {ScalesetId}", scaleset.ScalesetId);
            return OneFuzzResultVoid.Ok;
        }

        var nodesToReimage = new List<Node>();
        foreach (var node in nodes) {
            if (node.State != NodeState.Done) {
                continue;
            }

            if (node.DebugKeepNode) {
                _logTracer.LogWarning("not reimaging manually overridden node {MachineId} in scaleset {ScalesetId}", node.MachineId, scaleset.ScalesetId);
            } else {
                nodesToReimage.Add(node);
            }
        }

        if (!nodesToReimage.Any()) {
            _logTracer.LogInformation("no nodes to reimage {ScalesetId}", scaleset.ScalesetId);
            return OneFuzzResultVoid.Ok;
        }

        switch (disposalStrategy) {
            case NodeDisposalStrategy.Decommission:
                _logTracer.LogInformation("Skipping reimage of nodes in scaleset: {ScalesetId}, deleting nodes: {MachineIds} {InstanceIds}", scaleset.ScalesetId, string.Join(", ", nodesToReimage.Select(n => n.MachineId)), string.Join(", ", nodesToReimage.Select(n => n.InstanceId)));
                var deleteNodes = await _context.VmssOperations.DeleteNodes(scaleset.ScalesetId, nodesToReimage);
                if (!deleteNodes.IsOk) {
                    return deleteNodes;
                }
                await Async.Task.WhenAll(nodesToReimage
                    .Select(async node => {
                        await _context.NodeOperations.Delete(node, "Node decommissioned");
                    }));
                return OneFuzzResultVoid.Ok;

            case NodeDisposalStrategy.ScaleIn:
                var r = await _context.VmssOperations.ReimageNodes(scaleset.ScalesetId, nodesToReimage);
                if (!r.IsOk) {
                    return r;
                }

                await Async.Task.WhenAll(nodesToReimage
                    .Select(async node => {
                        var r = await _context.NodeOperations.ReleaseScaleInProtection(node);
                        if (r.IsOk) {
                            await _context.NodeOperations.Delete(node, "scaleset is scaling in");
                        }
                    }));
                return OneFuzzResultVoid.Ok;
            default:
                return OneFuzzResultVoid.Error(ErrorCode.INVALID_CONFIGURATION, $"Unhandled node disposal strategy: {disposalStrategy}");
        }
    }


    public async Async.Task<OneFuzzResultVoid> DeleteNodes(Scaleset scaleset, IEnumerable<Node> nodes, string reason) {
        if (nodes is null || !nodes.Any()) {
            _logTracer.LogInformation("no nodes to delete: scaleset_id: {ScalesetId}", scaleset.ScalesetId);
            return OneFuzzResultVoid.Ok;
        }

        // TODO: try to do this as one atomic operation:
        nodes = await Async.Task.WhenAll(nodes.Select(node => _context.NodeOperations.SetHalt(node)));

        if (scaleset.State == ScalesetState.Halt) {
            _logTracer.LogInformation("scaleset halting, ignoring deletion {ScalesetId}", scaleset.ScalesetId);
            return OneFuzzResultVoid.Ok;
        }

        var nodesToDelete = new List<Node>();
        foreach (var node in nodes) {
            if (node.DebugKeepNode) {
                _logTracer.LogWarning("not deleting manually overridden node {MachineId} in scaleset {ScalesetId}", node.MachineId, scaleset.ScalesetId);
            } else {
                nodesToDelete.Add(node);
            }
        }

        _logTracer.LogInformation("deleting nodes {ScalesetId} {MachineIds}", scaleset.ScalesetId, string.Join(", ", nodesToDelete.Select(n => n.MachineId)));
        var deleteNodes = await _context.VmssOperations.DeleteNodes(scaleset.ScalesetId, nodesToDelete);
        if (!deleteNodes.IsOk) {
            return deleteNodes;
        }
        await Async.Task.WhenAll(nodesToDelete
            .Select(async node => {
                await _context.NodeOperations.Delete(node, reason);
            }));
        return OneFuzzResultVoid.Ok;
    }

    public async Task<OneFuzzResult<Scaleset>> GetById(ScalesetId scalesetId) {
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
            _logTracer.LogInformation("resize finished {ScalesetId}", scaleset.ScalesetId);
            return await SetState(scaleset, ScalesetState.Running);
        } else {
            _logTracer.LogInformation("resize finished, waiting for nodes to check in {ScalesetId} ({NodeCount} of {Size} checked in)", scaleset.ScalesetId, nodeCount, scaleset.Size);
            return scaleset;
        }
    }

    private async Async.Task<Scaleset> ResizeGrow(Scaleset scaleset) {
        var resizeResult = await _context.VmssOperations.ResizeVmss(scaleset.ScalesetId, scaleset.Size);
        if (resizeResult.IsOk == false) {
            _logTracer.LogInformation("scaleset is mid-operation already {ScalesetId} {Error}", scaleset.ScalesetId, resizeResult.ErrorV);
        }
        return scaleset;
    }

    private async Async.Task<Scaleset> ResizeShrink(Scaleset scaleset, long? toRemove) {
        _logTracer.LogInformation("shrinking scaleset {ScalesetId} {ToRemove}", scaleset.ScalesetId, toRemove);

        if (!toRemove.HasValue) {
            return scaleset;
        } else {
            var queue = new ShrinkQueue(scaleset.ScalesetId, _context.Queue, _logTracer);
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

        var nodes = _context.NodeOperations.SearchStates(scalesetId: scaleset.ScalesetId);

        var result = new List<ScalesetNodeState>();
        await foreach (var node in nodes) {
            result.Add(new ScalesetNodeState(node.MachineId, node.InstanceId, node.State));
        }

        return result;
    }

    public IAsyncEnumerable<Scaleset> SearchStates(IEnumerable<ScalesetState> states)
        => QueryAsync(Query.EqualAnyEnum("state", states));

    public Async.Task<Scaleset> SetSize(Scaleset scaleset, long size) {
        var permittedSize = Math.Min(size, scaleset.Image.MaximumVmCount);
        if (permittedSize == scaleset.Size) {
            return Async.Task.FromResult(scaleset); // nothing to do
        }

        scaleset = scaleset with { Size = permittedSize };
        return SetState(scaleset, ScalesetState.Resize);
    }

    public async Async.Task<Scaleset> Shutdown(Scaleset scaleset) {
        var size = await _context.VmssOperations.GetVmssSize(scaleset.ScalesetId);
        if (size == null) {
            _logTracer.LogInformation("scale set shutdown: scaleset already deleted {ScalesetId}", scaleset.ScalesetId);
            return await Halt(scaleset);
        }

        _logTracer.LogInformation("scaleset shutdown {ScalesetId} {Size}", scaleset.ScalesetId, size);
        {
            var nodes = _context.NodeOperations.SearchStates(scalesetId: scaleset.ScalesetId);
            // TODO: Parallelization opportunity
            await nodes.ForEachAwaitAsync(_context.NodeOperations.SetShutdown);
        }

        _logTracer.LogInformation("checking for existing auto scale settings {ScalesetId}", scaleset.ScalesetId);

        var autoScalePolicy = _context.AutoScaleOperations.GetAutoscaleSettings(scaleset.ScalesetId);
        if (autoScalePolicy.IsOk && autoScalePolicy.OkV is AutoscaleSettingResource autoscaleSetting) {
            foreach (var profile in autoscaleSetting.Data.Profiles) {
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
                _logTracer.LogInformation("Getting nodes with scale in protection");

                try {
                    var vmsWithProtection = await _context.VmssOperations
                        .ListVmss(scaleset.ScalesetId)
                        .Where(vmResource => vmResource.Data?.ProtectionPolicy?.ProtectFromScaleIn is true)
                        .ToListAsync();

                    _logTracer.LogInformation("{VMsWithProtection}", JsonSerializer.Serialize(vmsWithProtection));
                    var numVmsWithProtection = vmsWithProtection.Count;
                    profile.Capacity.Minimum = numVmsWithProtection.ToString();
                    profile.Capacity.Default = numVmsWithProtection.ToString();
                } catch (Exception ex) {
                    _logTracer.LogError(ex, "Failed to list vmss for scaleset {ScalesetId}", scaleset.ScalesetId);
                }
            }

            var updatedAutoScale = await _context.AutoScaleOperations.UpdateAutoscale(autoscaleSetting.Data);
            if (!updatedAutoScale.IsOk) {
                _logTracer.LogError("Failed to update auto scale {Error}", updatedAutoScale.ErrorV);
            }
        } else if (!autoScalePolicy.IsOk) {
            _logTracer.LogError("{error}", autoScalePolicy.ErrorV);
        } else {
            _logTracer.LogInformation("No existing auto scale settings found for {ScalesetId}", scaleset.ScalesetId);
        }

        if (size == 0) {
            return await Halt(scaleset);
        }

        return scaleset;
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
