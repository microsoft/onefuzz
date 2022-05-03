using Azure;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;

namespace Microsoft.OneFuzz.Service;

public interface IVmssOperations {
    public Async.Task<OneFuzzResultVoid> UpdateExtensions(Guid name, IList<VirtualMachineScaleSetExtensionData> extensions);
    Async.Task<OneFuzzResultVoid> UpdateScaleInProtection(Guid name, Guid vmId, bool protectFromScaleIn);
    Async.Task<OneFuzzResult<string>> GetInstanceId(Guid name, Guid vmId);

}

public class VmssOperations : IVmssOperations {

    ILogTracer _log;
    ICreds _creds;

    public VmssOperations(ILogTracer log, ICreds creds) {
        _log = log;
        _creds = creds;
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
}
