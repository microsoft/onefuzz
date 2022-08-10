using Azure;
using Azure.Core;
using System.Net;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Rest.Azure;
using Azure.Data.Tables;

namespace Microsoft.OneFuzz.Service;

public interface IVmssOperations {
    Async.Task<OneFuzzResultVoid> UpdateScaleInProtection(Guid name, Guid vmId, bool protectFromScaleIn);
    Async.Task<OneFuzzResult<string>> GetInstanceId(Guid name, Guid vmId);
    Async.Task<OneFuzzResultVoid> UpdateExtensions(Guid name, IList<VirtualMachineScaleSetExtensionData> extensions);
    Async.Task<VirtualMachineScaleSetData?> GetVmss(Guid name);

    Async.Task<IReadOnlyList<string>> ListAvailableSkus(string region);

    Async.Task<bool> DeleteVmss(Guid name, bool? forceDeletion = null);

    Async.Task<IDictionary<Guid, string>> ListInstanceIds(Guid name);

    Async.Task<long?> GetVmssSize(Guid name);

    Async.Task<OneFuzzResultVoid> ResizeVmss(Guid name, long capacity);

    Async.Task<OneFuzzResultVoid> CreateVmss(
        string location,
        Guid name,
        string vmSku,
        long vmCount,
        string image,
        string networkId,
        bool? spotInstance,
        bool ephemeralOsDisks,
        IList<VirtualMachineScaleSetExtensionData>? extensions,
        string password,
        string sshPublicKey,
        IDictionary<string, string> tags);
}

public class VmssOperations : IVmssOperations {
    private readonly ILogTracer _log;
    private readonly ICreds _creds;
    private readonly IImageOperations _imageOps;
    private readonly IServiceConfig _serviceConfig;
    private readonly IMemoryCache _cache;

    public VmssOperations(ILogTracer log, IOnefuzzContext context, IMemoryCache cache) {
        _log = log;
        _creds = context.Creds;
        _imageOps = context.ImageOperations;
        _serviceConfig = context.ServiceConfiguration;
        _cache = cache;
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

    public async Async.Task<long?> GetVmssSize(Guid name) {
        var vmss = await GetVmss(name);
        if (vmss == null) {
            return null;
        }
        return vmss.Sku.Capacity;
    }

    public async Async.Task<OneFuzzResultVoid> ResizeVmss(Guid name, long capacity) {
        var canUpdate = await CheckCanUpdate(name);
        if (canUpdate.IsOk) {
            _log.Info($"updating VM count - name: {name} vm_count: {capacity}");
            var scalesetResource = GetVmssResource(name);
            var patch = new VirtualMachineScaleSetPatch();
            patch.Sku.Capacity = capacity;
            await scalesetResource.UpdateAsync(WaitUntil.Started, patch);
            return OneFuzzResultVoid.Ok;
        } else {
            return OneFuzzResultVoid.Error(canUpdate.ErrorV);
        }
    }


    private VirtualMachineScaleSetResource GetVmssResource(Guid name) {
        var resourceGroup = _creds.GetBaseResourceGroup();
        var id = VirtualMachineScaleSetResource.CreateResourceIdentifier(_creds.GetSubscription(), resourceGroup, name.ToString());
        return _creds.ArmClient.GetVirtualMachineScaleSetResource(id);
    }


    public async Async.Task<VirtualMachineScaleSetData?> GetVmss(Guid name) {
        try {
            var res = await GetVmssResource(name).GetAsync();
            _log.Verbose($"getting vmss: {name}");
            return res.Value.Data;
        } catch (Exception ex) when (ex is RequestFailedException) {
            return null;
        }
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
            return OneFuzzResultVoid.Ok;

        } else {
            return OneFuzzResultVoid.Error(canUpdate.ErrorV);
        }
    }

    public async Async.Task<IDictionary<Guid, string>> ListInstanceIds(Guid name) {
        _log.Verbose($"get instance IDs for scaleset {name}");
        try {
            return await GetVmssResource(name)
                .GetVirtualMachineScaleSetVms()
                .ToDictionaryAsync(vm => Guid.Parse(vm.Data.VmId), vm => vm.Data.InstanceId);
        } catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound || ex.ErrorCode == "NotFound") {
            _log.Exception(ex, $"scaleset does not exist: {name}");
            return new Dictionary<Guid, string>();
        }
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
            var instanceVm = res.OkV;
            instanceVm.Data.ProtectionPolicy ??= new();
            if (instanceVm.Data.ProtectionPolicy.ProtectFromScaleIn != protectFromScaleIn) {
                instanceVm.Data.ProtectionPolicy.ProtectFromScaleIn = protectFromScaleIn;
                var vmCollection = GetVmssResource(name).GetVirtualMachineScaleSetVms();
                try {
                    await vmCollection.CreateOrUpdateAsync(WaitUntil.Started, instanceVm.Data.InstanceId, instanceVm.Data);
                    return OneFuzzResultVoid.Ok;
                } catch {
                    var msg = $"unable to set protection policy on: {vmId}:{instanceVm.Id}";
                    return OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_UPDATE, msg);
                }
            } else {
                _log.Info($"scale in protection was already set to {protectFromScaleIn} on vm {vmId} for scaleset {name}");
                return OneFuzzResultVoid.Ok;
            }
        }
    }

    public async Async.Task<OneFuzzResultVoid> CreateVmss(
        string location,
        Guid name,
        string vmSku,
        long vmCount,
        string image,
        string networkId,
        bool? spotInstance,
        bool ephemeralOsDisks,
        IList<VirtualMachineScaleSetExtensionData>? extensions,
        string password,
        string sshPublicKey,
        IDictionary<string, string> tags) {
        var vmss = await GetVmss(name);
        if (vmss is not null) {
            return OneFuzzResultVoid.Ok;
        }
        _log.Info($"creating VM name: {name}, vm_sku: {vmSku}, vm_count: {vmCount}, image: {image}, subnet: {networkId}, spot_instance: {spotInstance}");
        var getOsResult = await _imageOps.GetOs(location, image);

        if (!getOsResult.IsOk) {
            return getOsResult.ErrorV;
        }

        var vmssData = new VirtualMachineScaleSetData(location) {
            DoNotRunExtensionsOnOverprovisionedVms = false,
            Sku = new ComputeSku() { Name = vmSku },
            Overprovision = false,
            SinglePlacementGroup = false,
            UpgradePolicy = new UpgradePolicy() { Mode = UpgradeMode.Manual },
            Identity = new ManagedServiceIdentity(managedServiceIdentityType: ManagedServiceIdentityType.UserAssigned),
        };
        vmssData.Identity.UserAssignedIdentities.Add(_creds.GetScalesetIdentityResourcePath(), new UserAssignedIdentity());
        vmssData.VirtualMachineProfile = new VirtualMachineScaleSetVmProfile() { Priority = VirtualMachinePriorityTypes.Regular };
        var imageRef = new ImageReference();

        if (image.StartsWith('/')) {
            imageRef.Id = image;
        } else {
            var info = IImageOperations.GetImageInfo(image);
            imageRef.Publisher = info.Publisher;
            imageRef.Offer = info.Offer;
            imageRef.Sku = info.Sku;
            imageRef.Version = info.Version;
        }
        vmssData.VirtualMachineProfile.StorageProfile = new VirtualMachineScaleSetStorageProfile() { ImageReference = imageRef };
        vmssData.VirtualMachineProfile.OSProfile = new VirtualMachineScaleSetOSProfile() { ComputerNamePrefix = "node", AdminUsername = "onefuzz" };

        var networkConfiguration = new VirtualMachineScaleSetNetworkConfiguration("onefuzz-nic") { Primary = true };
        var ipConfig = new VirtualMachineScaleSetIPConfiguration("onefuzz-ip-config");
        ipConfig.SubnetId = new ResourceIdentifier(networkId);
        networkConfiguration.IPConfigurations.Add(ipConfig);

        vmssData.VirtualMachineProfile.NetworkProfile = new VirtualMachineScaleSetNetworkProfile();
        vmssData.VirtualMachineProfile.NetworkProfile.NetworkInterfaceConfigurations.Add(networkConfiguration);

        if (extensions is not null) {
            vmssData.VirtualMachineProfile.ExtensionProfile = new VirtualMachineScaleSetExtensionProfile();
            foreach (var e in extensions) {
                vmssData.VirtualMachineProfile.ExtensionProfile.Extensions.Add(e);
            }
        }

        switch (getOsResult.OkV) {
            case Os.Windows:
                vmssData.VirtualMachineProfile.OSProfile.AdminPassword = password;
                break;
            case Os.Linux:
                vmssData.VirtualMachineProfile.OSProfile.LinuxConfiguration = new LinuxConfiguration();
                vmssData.VirtualMachineProfile.OSProfile.LinuxConfiguration.DisablePasswordAuthentication = true;
                var i = new SshPublicKeyInfo() { KeyData = sshPublicKey, Path = "/home/onefuzz/.ssh/authorized_keys" };
                vmssData.VirtualMachineProfile.OSProfile.LinuxConfiguration.SshPublicKeys.Add(i);
                break;
            default:
                return OneFuzzResultVoid.Error(ErrorCode.INVALID_CONFIGURATION, $"unhandled OS: {getOsResult.OkV} in image: {image}");
        }

        if (ephemeralOsDisks) {
            vmssData.VirtualMachineProfile.StorageProfile.OSDisk = new VirtualMachineScaleSetOSDisk(DiskCreateOptionTypes.FromImage);
            vmssData.VirtualMachineProfile.StorageProfile.OSDisk.DiffDiskSettings = new DiffDiskSettings();
            vmssData.VirtualMachineProfile.StorageProfile.OSDisk.DiffDiskSettings.Option = DiffDiskOptions.Local;
            vmssData.VirtualMachineProfile.StorageProfile.OSDisk.Caching = CachingTypes.ReadOnly;
        }

        if (spotInstance.HasValue && spotInstance.Value) {
            // Setting max price to -1 means it won't be evicted because of
            // price.
            //
            // https://docs.microsoft.com/en-us/azure/
            //   virtual-machine-scale-sets/use-spot#resource-manager-templates
            vmssData.VirtualMachineProfile.EvictionPolicy = VirtualMachineEvictionPolicyTypes.Deallocate;
            vmssData.VirtualMachineProfile.Priority = VirtualMachinePriorityTypes.Spot;
            vmssData.VirtualMachineProfile.BillingMaxPrice = 1.0;
        }

        foreach (var tag in tags) {
            vmssData.Tags.Add(tag);
        }

        if (_serviceConfig.OneFuzzOwner is not null) {
            vmssData.Tags.Add("OWNER", _serviceConfig.OneFuzzOwner);
        }

        try {
            var rg = _creds.GetResourceGroupResource();
            var createUpdate = await rg.GetVirtualMachineScaleSets().CreateOrUpdateAsync(WaitUntil.Started, name.ToString(), vmssData);
            if (createUpdate.GetRawResponse().IsError) {
                var msg = $"Failed to create new scaleset due to {createUpdate.GetRawResponse().ReasonPhrase}";
                _log.Error(msg);
                return OneFuzzResultVoid.Error(ErrorCode.VM_CREATE_FAILED, new[] { msg });
            } else {
                return OneFuzzResultVoid.Ok;
            }
        } catch (Exception ex) {
            _log.Exception(ex);
            return OneFuzzResultVoid.Error(ErrorCode.VM_CREATE_FAILED, new[] { ex.Message });
        }
    }

    public Async.Task<IReadOnlyList<string>> ListAvailableSkus(string region)
        => _cache.GetOrCreateAsync<IReadOnlyList<string>>($"compute-skus-{region}", async entry => {
            entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(10));

            var sub = _creds.GetSubscriptionResource();
            var skus = sub.GetResourceSkusAsync(filter: TableClient.CreateQueryFilter($"location eq '{region}'"));

            var skuNames = new List<string>();
            await foreach (var sku in skus) {
                var available = true;
                if (sku.Restrictions is not null) {
                    foreach (var restriction in sku.Restrictions) {
                        if (restriction.RestrictionsType == ResourceSkuRestrictionsType.Location &&
                            restriction.Values.Contains(region, StringComparer.OrdinalIgnoreCase)) {
                            available = false;
                            break;
                        }
                    }
                }

                if (available) {
                    skuNames.Add(sku.Name);
                }
            }

            return skuNames;
        });
}
