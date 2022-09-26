using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Rest.Azure;

namespace Microsoft.OneFuzz.Service;

public interface IVmssOperations {
    Async.Task<OneFuzzResultVoid> UpdateScaleInProtection(Guid name, Guid vmId, bool protectFromScaleIn);
    Async.Task<OneFuzzResult<string>> GetInstanceId(Guid name, Guid vmId);
    Async.Task<OneFuzzResultVoid> UpdateExtensions(Guid name, IList<VirtualMachineScaleSetExtensionData> extensions);
    Async.Task<VirtualMachineScaleSetData?> GetVmss(Guid name);

    Async.Task<IReadOnlyList<string>> ListAvailableSkus(Region region);

    Async.Task<bool> DeleteVmss(Guid name, bool? forceDeletion = null);

    Async.Task<IDictionary<Guid, string>> ListInstanceIds(Guid name);

    Async.Task<long?> GetVmssSize(Guid name);

    Async.Task<OneFuzzResultVoid> ResizeVmss(Guid name, long capacity);

    Async.Task<OneFuzzResultVoid> CreateVmss(
        Region location,
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

    Async.Task<List<string>?> ListVmss(Guid name, Func<VirtualMachineScaleSetVmResource, bool>? filter);
    Async.Task<OneFuzzResultVoid> ReimageNodes(Guid scalesetId, IReadOnlySet<Guid> machineIds);
    Async.Task DeleteNodes(Guid scalesetId, IReadOnlySet<Guid> machineIds);
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
        } catch (RequestFailedException ex) when (ex.Status == 404) {
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
            var patch = new VirtualMachineScaleSetPatch() {
                VirtualMachineProfile =
                    new VirtualMachineScaleSetUpdateVmProfile() { ExtensionProfile = new VirtualMachineScaleSetExtensionProfile() }
            };

            foreach (var ext in extensions) {
                patch.VirtualMachineProfile.ExtensionProfile.Extensions.Add(ext);
            }
            _ = await res.UpdateAsync(WaitUntil.Started, patch);
            _log.Info($"VM extensions updated: {name}");
            return OneFuzzResultVoid.Ok;

        } else {
            return OneFuzzResultVoid.Error(canUpdate.ErrorV);
        }
    }

    public async Async.Task<IDictionary<Guid, string>> ListInstanceIds(Guid name) {
        _log.Verbose($"get instance IDs for scaleset {name}");
        var results = new Dictionary<Guid, string>();
        VirtualMachineScaleSetResource res;
        try {
            var r = await GetVmssResource(name).GetAsync();
            res = r.Value;
        } catch (Exception ex) when (ex is RequestFailedException) {
            _log.Verbose($"vm does not exist {name}");
            return results;
        }

        if (res is null) {
            _log.Verbose($"vm does not exist {name}");
            return results;
        } else {
            try {
                await foreach (var instance in res!.GetVirtualMachineScaleSetVms().AsAsyncEnumerable()) {
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
        Region location,
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

        var vmssData = new VirtualMachineScaleSetData(location) {
            DoNotRunExtensionsOnOverprovisionedVms = false,
            UpgradePolicy = new() {
                Mode = UpgradeMode.Manual,
            },
            Sku = new() {
                Name = vmSku,
                Tier = "Standard",
                Capacity = vmCount,
            },
            Overprovision = false,
            SinglePlacementGroup = false,
            Identity = new(ManagedServiceIdentityType.UserAssigned) {
                UserAssignedIdentities = {
                    { _creds.GetScalesetIdentityResourcePath(), new UserAssignedIdentity() },
                },
            },
            VirtualMachineProfile = new() {
                Priority = VirtualMachinePriorityTypes.Regular,
                StorageProfile = new() {
                    ImageReference = imageRef,
                },
                OSProfile = new() {
                    ComputerNamePrefix = "node",
                    AdminUsername = "onefuzz",
                },
                NetworkProfile = new() {
                    NetworkInterfaceConfigurations = {
                        new("onefuzz-nic") {
                            Primary = true,
                            IPConfigurations = {
                                new("onefuzz-ip-config") {
                                    SubnetId = new ResourceIdentifier(networkId)
                                },
                            },
                        },
                    },
                },
            },
        };

        if (extensions is not null) {
            vmssData.VirtualMachineProfile.ExtensionProfile = new();
            foreach (var e in extensions) {
                vmssData.VirtualMachineProfile.ExtensionProfile.Extensions.Add(e);
            }
        }

        switch (getOsResult.OkV) {
            case Os.Windows:
                vmssData.VirtualMachineProfile.OSProfile.AdminPassword = password;
                break;
            case Os.Linux:
                vmssData.VirtualMachineProfile.OSProfile.LinuxConfiguration = new() {
                    DisablePasswordAuthentication = true,
                    SshPublicKeys = {
                        new() {
                            KeyData = sshPublicKey,
                            Path = "/home/onefuzz/.ssh/authorized_keys",
                        },
                    }
                };
                break;
            default:
                return OneFuzzResultVoid.Error(ErrorCode.INVALID_CONFIGURATION, $"unhandled OS: {getOsResult.OkV} in image: {image}");
        }

        if (ephemeralOsDisks) {
            vmssData.VirtualMachineProfile.StorageProfile.OSDisk = new(DiskCreateOptionTypes.FromImage) {
                DiffDiskSettings = new DiffDiskSettings {
                    Option = DiffDiskOptions.Local,
                },
                Caching = CachingTypes.ReadOnly,
            };
        }

        if (spotInstance.HasValue && spotInstance.Value) {
            // Setting max price to -1 means it won't be evicted because of
            // price.
            //
            // https://docs.microsoft.com/en-us/azure/
            //   virtual-machine-scale-sets/use-spot#resource-manager-templates
            vmssData.VirtualMachineProfile.EvictionPolicy = VirtualMachineEvictionPolicyTypes.Delete;
            vmssData.VirtualMachineProfile.Priority = VirtualMachinePriorityTypes.Spot;
            vmssData.VirtualMachineProfile.BillingMaxPrice = -1.0;
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

    public async Task<List<string>?> ListVmss(Guid name, Func<VirtualMachineScaleSetVmResource, bool>? filter) {
        try {
            var vmss = await _creds.GetResourceGroupResource().GetVirtualMachineScaleSetAsync(name.ToString());
            return vmss.Value.GetVirtualMachineScaleSetVms().ToEnumerable()
                .Where(vm => filter == null || filter(vm))
                .Select(vm => vm.Data.InstanceId)
                .ToList();
        } catch (RequestFailedException ex) {
            _log.Exception(ex, $"cloud error listing vmss: {name}");
        }
        return null;
    }

    public Async.Task<IReadOnlyList<string>> ListAvailableSkus(Region region)
        => _cache.GetOrCreateAsync<IReadOnlyList<string>>($"compute-skus-{region}", async entry => {
            entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(10));

            var sub = _creds.GetSubscriptionResource();
            var skus = sub.GetResourceSkusAsync(filter: TableClient.CreateQueryFilter($"location eq {region.String}"));

            var skuNames = new List<string>();
            await foreach (var sku in skus) {
                var available = true;
                if (sku.Restrictions is not null) {
                    foreach (var restriction in sku.Restrictions) {
                        if (restriction.RestrictionsType == ResourceSkuRestrictionsType.Location &&
                            restriction.Values.Contains(region.String, StringComparer.OrdinalIgnoreCase)) {
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

    public async Async.Task<OneFuzzResultVoid> ReimageNodes(Guid scalesetId, IReadOnlySet<Guid> machineIds) {
        var result = await CheckCanUpdate(scalesetId);
        if (!result.IsOk) {
            return OneFuzzResultVoid.Error(result.ErrorV);
        }

        var instanceIds = new HashSet<string>();
        var machineToInstance = await ListInstanceIds(scalesetId);
        foreach (var machineId in machineIds) {
            if (machineToInstance.TryGetValue(machineId, out var instanceId)) {
                _ = instanceIds.Add(instanceId);
            } else {
                _log.Info($"unable to find instance ID for {scalesetId}:{machineId}");
            }
        }

        if (!instanceIds.Any()) {
            return OneFuzzResultVoid.Ok;
        }

        var subscription = _creds.GetSubscription();
        var resourceGroup = _creds.GetBaseResourceGroup();
        var vmssId = VirtualMachineScaleSetResource.CreateResourceIdentifier(
            subscription, resourceGroup, scalesetId.ToString());

        var computeClient = _creds.ArmClient;
        var vmssResource = computeClient.GetVirtualMachineScaleSetResource(vmssId);

        // Nodes that must be are 'upgraded' before the reimage. This call makes sure
        // the instance is up-to-date with the VMSS model.
        // The expectation is that these requests are queued and handled subsequently.
        // The VMSS Team confirmed this expectation and testing supports it, as well.
        _log.Info($"upgrading VMSS ndoes - name: {scalesetId} ids: {string.Join(", ", instanceIds)}");
        var r = await vmssResource.UpdateInstancesAsync(
            WaitUntil.Started,
            new VirtualMachineScaleSetVmInstanceRequiredIds(instanceIds));
        if (r.GetRawResponse().IsError) {
            _log.Error($"failed to start update instance for scaleset {scalesetId} due to {r.GetRawResponse().ReasonPhrase}");
        }

        _log.Info($"reimaging VMSS nodes - name: {scalesetId} ids: {string.Join(", ", instanceIds)}");

        // very weird API here…
        var reqInstanceIds = new VirtualMachineScaleSetVmInstanceIds();
        foreach (var instanceId in instanceIds) {
            reqInstanceIds.InstanceIds.Add(instanceId);
        }

        r = await vmssResource.ReimageAllAsync(WaitUntil.Started, reqInstanceIds);
        if (r.GetRawResponse().IsError) {
            _log.Error($"failed to start reimage all for scaleset {scalesetId} due to {r.GetRawResponse().ReasonPhrase}");
        }
        return OneFuzzResultVoid.Ok;
    }

    public async Async.Task DeleteNodes(Guid scalesetId, IReadOnlySet<Guid> machineIds) {
        var result = await CheckCanUpdate(scalesetId);
        if (!result.IsOk) {
            throw new Exception($"cannot delete nodes from scaleset {scalesetId}: {result.ErrorV}");
        }

        var instanceIds = new HashSet<string>();
        var machineToInstance = await ListInstanceIds(scalesetId);
        foreach (var machineId in machineIds) {
            if (machineToInstance.TryGetValue(machineId, out var instanceId)) {
                _ = instanceIds.Add(instanceId);
            } else {
                _log.Info($"unable to find instance ID for {scalesetId}:{machineId}");
            }
        }

        if (!instanceIds.Any()) {
            return;
        }

        var subscription = _creds.GetSubscription();
        var resourceGroup = _creds.GetBaseResourceGroup();
        var vmssId = VirtualMachineScaleSetResource.CreateResourceIdentifier(
            subscription, resourceGroup, scalesetId.ToString());

        var computeClient = _creds.ArmClient;
        var vmssResource = computeClient.GetVirtualMachineScaleSetResource(vmssId);

        _log.Info($"deleting scaleset VMs - name: {scalesetId} ids: {instanceIds}");
        var r = await vmssResource.DeleteInstancesAsync(
            WaitUntil.Started,
            new VirtualMachineScaleSetVmInstanceRequiredIds(instanceIds));

        if (r.GetRawResponse().IsError) {
            _log.Error($"failed to start deletion of scaleset {scalesetId} due to {r.GetRawResponse().ReasonPhrase}");
        }
        return;
    }
}
