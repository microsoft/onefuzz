using Azure;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Microsoft.Rest.Azure;

namespace Microsoft.OneFuzz.Service;

public interface IVmssOperations {
<<<<<<< HEAD
    public Async.Task<OneFuzzResultVoid> UpdateExtensions(Guid name, IList<VirtualMachineScaleSetExtensionData> extensions);
    Async.Task<OneFuzzResultVoid> UpdateScaleInProtection(Guid name, Guid vmId, bool protectFromScaleIn);
    Async.Task<OneFuzzResult<string>> GetInstanceId(Guid name, Guid vmId);
=======
    Async.Task<OneFuzzResultVoid> UpdateExtensions(Guid name, IList<VirtualMachineScaleSetExtensionData> extensions);
    Async.Task<VirtualMachineScaleSetData> GetVmss(Guid name);
>>>>>>> 8c73f84b87fa5ddb59d45e398f5e8475aaf43543

    Async.Task<bool> DeleteVmss(Guid name, bool? forceDeletion = null);


    Async.Task<IDictionary<Guid, string>> ListInstanceIds(Guid name);
}

public class VmssOperations : IVmssOperations {

    ILogTracer _log;
    ICreds _creds;

    public VmssOperations(ILogTracer log, ICreds creds) {
        _log = log;
        _creds = creds;
    }

    public async Async.Task<bool> DeleteVmss(Guid name, bool? forceDeletion = null) {
        var r = GetVmssResource(name);
        var result = await r.DeleteAsync(WaitUntil.Started, forceDeletion: forceDeletion);
        var raw = result.GetRawResponse();
        if (raw.IsError) {
            _log.WithHttpStatus((raw.Status, raw.ReasonPhrase)).Error($"Failed to delete vmss: {name}");
            return false;
        } else {
            return true;
        }
    }


    private VirtualMachineScaleSetResource GetVmssResource(Guid name) {
        var resourceGroup = _creds.GetBaseResourceGroup();
        var id = VirtualMachineScaleSetResource.CreateResourceIdentifier(_creds.GetSubscription(), resourceGroup, name.ToString());
        return _creds.ArmClient.GetVirtualMachineScaleSetResource(id);
    }


    public async Async.Task<VirtualMachineScaleSetData> GetVmss(Guid name) {
        var res = GetVmssResource(name);
        _log.Verbose($"getting vmss: {name}");
        var r = await res.GetAsync();
        return r.Value.Data;
    }

    public async Async.Task<OneFuzzResult<VirtualMachineScaleSetData>> CheckCanUpdate(Guid name) {
        var vmss = await GetVmss(name);
        if (vmss is null) {
            return OneFuzzResult<VirtualMachineScaleSetData>.Error(ErrorCode.UNABLE_TO_UPDATE, $"vmss not found: {name}");
        }
        if (vmss.ProvisioningState == "Updating") {
            return OneFuzzResult<VirtualMachineScaleSetData>.Error(ErrorCode.UNABLE_TO_UPDATE, $"vmss is in updating state: {name}");
        }
        return OneFuzzResult<VirtualMachineScaleSetData>.Ok(vmss);
    }


    public async Async.Task<OneFuzzResultVoid> UpdateExtensions(Guid name, IList<VirtualMachineScaleSetExtensionData> extensions) {
        var canUpdate = await CheckCanUpdate(name);
        if (canUpdate.IsOk) {
            _log.Info($"updating VM extensions: {name}");
            var res = GetVmssResource(name);
            var patch = new VirtualMachineScaleSetPatch();

            foreach (var ext in extensions) {
                patch.VirtualMachineProfile.ExtensionProfile.Extensions.Add(ext);
            }
            var _ = await res.UpdateAsync(WaitUntil.Started, patch);
            _log.Info($"VM extensions updated: {name}");
            return OneFuzzResultVoid.Ok();

        } else {
            return OneFuzzResultVoid.Error(canUpdate.ErrorV);
        }
    }

<<<<<<< HEAD
    public async Async.Task<OneFuzzResult<string>> GetInstanceId(Guid name, Guid vmId) {
        _log.Info($"get instance ID for scaleset node: {name}:{vmId}");
        var vmIdStr = vmId.ToString();
        var vmss = (await _creds.GetResourceGroupResource().GetVirtualMachineScaleSetAsync(name.ToString())).Value;
        var instance = await vmss.GetVirtualMachineScaleSetVms()
            .SelectAwait(async instance => await instance.GetAsync())
            .Where(instance => string.Equals(instance.Value.Data.VmId, vmIdStr))
            .FirstOrDefaultAsync();

        return instance switch {
            Response<VirtualMachineScaleSetVmResource> i =>
                OneFuzzResult<string>.Ok(i.Value.Data.VmId),
            _ =>
                OneFuzzResult<string>.Error(
                    ErrorCode.UNABLE_TO_FIND,
                    $"unable to find scaleset machine: {name}:{vmId}"
                )
        };
    }

    public async Async.Task<OneFuzzResultVoid> UpdateScaleInProtection(Guid name, Guid vmId, bool protectFromScaleIn) {
        var instanceIdResult = await GetInstanceId(name, vmId);

        if (!instanceIdResult.IsOk) {
            return OneFuzzResultVoid.Error(instanceIdResult.ErrorV);
        }
        var instanceId = instanceIdResult.OkV;

        var vmss = (await _creds.GetResourceGroupResource().GetVirtualMachineScaleSetAsync(name.ToString())).Value;
        var instanceVmResult = await vmss.GetVirtualMachineScaleSetVmAsync(instanceId);

        if (instanceVmResult == null) {
            return OneFuzzResultVoid.Error(
                ErrorCode.UNABLE_TO_FIND,
                $"unable to find vm instance: {name}:{instanceId}"
            );
        }
        var instanceVm = instanceVmResult.Value;

        var newProtectionPolicy = instanceVm.Data.ProtectionPolicy ?? new VirtualMachineScaleSetVmProtectionPolicy();
        newProtectionPolicy.ProtectFromScaleIn = protectFromScaleIn;

        instanceVm.Data.ProtectionPolicy = newProtectionPolicy;

        var update = await vmss.GetVirtualMachineScaleSetVms().CreateOrUpdateAsync(WaitUntil.Started, instanceId, instanceVm.Data);
        var unableToUpdate = OneFuzzResultVoid.Error(
                ErrorCode.UNABLE_TO_UPDATE,
                $"unable to set protection policy on {vmId}:{instanceId}"
        );
        if (update == null) {
            return unableToUpdate;
        }

        var rawResponse = update.GetRawResponse();
        if (rawResponse.IsError) {
            var errorContent = rawResponse.Content.ToString();
            const string instanceNotFound = " is not an active Virtual Machine Scale Set VM instanceId.";
            if (errorContent.Contains(instanceNotFound)
            && instanceVm.Data.ProtectionPolicy.ProtectFromScaleIn == false
            && instanceVm.Data.ProtectionPolicy.ProtectFromScaleIn == protectFromScaleIn) {
                _log.Info($"Tried to remove scale in protection on node {instanceId} but the instance no longer exists");
                return OneFuzzResultVoid.Ok();
            }
            return unableToUpdate;
        }

        _log.Info($"Successfully set scale in protection on node {vmId} to {protectFromScaleIn}");
        return OneFuzzResultVoid.Ok();
    }
=======
    public async Async.Task<IDictionary<Guid, string>> ListInstanceIds(Guid name) {
        _log.Verbose($"get instance IDs for scaleset {name}");
        var results = new Dictionary<Guid, string>();

        var res = GetVmssResource(name);
        if (res is null) {
            _log.Verbose($"vm does not exist {name}");
            return results;
        } else {
            try {
                await foreach (var instance in res.GetVirtualMachineScaleSetVms().AsAsyncEnumerable()) {
                    if (instance is not null) {
                        Guid key;
                        if (Guid.TryParse(instance.Data.VmId, out key)) {
                            results[key] = instance.Data.InstanceId;
                        } else {
                            _log.Error($"failed to convert vmId {instance.Data.VmId} to Guid");
                        }
                    }
                }
            } catch (CloudException ex) {
                _log.Exception(ex, $"vm does not exist {name}");
            }
        }
        return results;
    }

    public async Async.Task<OneFuzzResult<VirtualMachineScaleSetVmResource>> GetInstanceVm(Guid name, Guid vmId) {
        _log.Info($"get instance ID for scaleset node: {name}:{vmId}");
        var scaleSet = GetVmssResource(name);
        try {
            await foreach (var vm in scaleSet.GetVirtualMachineScaleSetVms().AsAsyncEnumerable()) {
                var response = await vm.GetAsync();
                if (!response.Value.HasData) {
                    return OneFuzzResult<VirtualMachineScaleSetVmResource>.Error(ErrorCode.UNABLE_TO_FIND, $"failed to get vm data");
                }

                if (response.Value.Data.VmId == vmId.ToString()) {
                    return OneFuzzResult<VirtualMachineScaleSetVmResource>.Ok(response);
                }
            }
        } catch (CloudException ex) {
            _log.Exception(ex, $"unable to find vm instance: {name}:{vmId}");
            return OneFuzzResult<VirtualMachineScaleSetVmResource>.Error(ErrorCode.UNABLE_TO_FIND, $"unable to find vm instance: {name}:{vmId}");
        }
        return OneFuzzResult<VirtualMachineScaleSetVmResource>.Error(ErrorCode.UNABLE_TO_FIND, $"unable to find scaleset machine: {name}:{vmId}");
    }

    public async Async.Task<OneFuzzResult<string>> GetInstanceId(Guid name, Guid vmId) {
        var vm = await GetInstanceVm(name, vmId);
        if (vm.IsOk) {
            return OneFuzzResult<string>.Ok(vm.OkV!.Data.InstanceId);
        } else {
            return OneFuzzResult<string>.Error(vm.ErrorV);
        }
    }


    public async Async.Task<OneFuzzResultVoid> UpdateScaleInProtection(Guid name, Guid vmId, bool protectFromScaleIn) {
        var res = await GetInstanceVm(name, vmId);
        if (!res.IsOk) {
            return OneFuzzResultVoid.Error(res.ErrorV);
        } else {
            VirtualMachineScaleSetVmProtectionPolicy newProtectionPolicy;
            var instanceVm = res.OkV!;
            if (instanceVm.Data.ProtectionPolicy is not null) {
                newProtectionPolicy = instanceVm.Data.ProtectionPolicy;
                newProtectionPolicy.ProtectFromScaleIn = protectFromScaleIn;
            } else {
                newProtectionPolicy = new VirtualMachineScaleSetVmProtectionPolicy() { ProtectFromScaleIn = protectFromScaleIn };
            }
            instanceVm.Data.ProtectionPolicy = newProtectionPolicy;

            var scaleSet = GetVmssResource(name);

            VirtualMachineScaleSetVmInstanceRequiredIds ids = new VirtualMachineScaleSetVmInstanceRequiredIds(new[] { instanceVm.Data.InstanceId });
            var updateRes = await scaleSet.UpdateInstancesAsync(WaitUntil.Started, ids);

            //TODO: finish this after UpdateInstance method is fixed
            //https://github.com/Azure/azure-sdk-for-net/issues/28491

            throw new NotImplementedException("Update instance does not work as expected. See https://github.com/Azure/azure-sdk-for-net/issues/28491");
        }
    }


>>>>>>> 8c73f84b87fa5ddb59d45e398f5e8475aaf43543
}
