using Azure;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Microsoft.Rest.Azure;

namespace Microsoft.OneFuzz.Service;

public interface IVmssOperations {
    Async.Task<OneFuzzResultVoid> UpdateScaleInProtection(Guid name, Guid vmId, bool protectFromScaleIn);
    Async.Task<OneFuzzResult<string>> GetInstanceId(Guid name, Guid vmId);
    Async.Task<OneFuzzResultVoid> UpdateExtensions(Guid name, IList<VirtualMachineScaleSetExtensionData> extensions);
    Async.Task<VirtualMachineScaleSetData> GetVmss(Guid name);

    Async.Task<bool> DeleteVmss(Guid name, bool? forceDeletion = null);


    Async.Task<IDictionary<Guid, string>> ListInstanceIds(Guid name);
}

public class VmssOperations : IVmssOperations {

    string INSTANCE_NOT_FOUND = " is not an active Virtual Machine Scale Set VM instanceId.";

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
            } catch (Exception ex) when (ex is RequestFailedException || ex is CloudException) {
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
        } catch (Exception ex) when (ex is RequestFailedException || ex is CloudException) {
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
            var vmCollection = scaleSet.GetVirtualMachineScaleSetVms();
            try {
                var r = await vmCollection.CreateOrUpdateAsync(WaitUntil.Started, instanceVm.Data.InstanceId, instanceVm.Data);
                if (r.GetRawResponse().IsError) {
                    var msg = $"failed to update scale in protection on vm {vmId} for scaleset {name}";
                    _log.WithHttpStatus((r.GetRawResponse().Status, r.GetRawResponse().ReasonPhrase)).Error(msg);
                    return OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_UPDATE, msg);
                } else {
                    return OneFuzzResultVoid.Ok();
                }
            } catch (Exception ex) when (ex is RequestFailedException || ex is CloudException) {

                if (ex.Message.Contains(INSTANCE_NOT_FOUND) && protectFromScaleIn == false) {
                    _log.Info($"Tried to remove scale in protection on node {name} {vmId} but instance no longer exists");
                    return OneFuzzResultVoid.Ok();
                } else {
                    var msg = $"failed to update scale in protection on vm {vmId} for scaleset {name}";
                    _log.Exception(ex, msg);
                    return OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_UPDATE, ex.Message);
                }
            }
        }
    }


}
