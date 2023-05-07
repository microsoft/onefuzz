using System.Net;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure;
using Azure.Core;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Rest.Azure;

namespace Microsoft.OneFuzz.Service;

public interface IVmssOperations {
    Async.Task<OneFuzzResultVoid> UpdateScaleInProtection(Scaleset scaleset, string instanceId, bool protectFromScaleIn);
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
        ImageReference image,
        string networkId,
        bool? spotInstance,
        bool ephemeralOsDisks,
        IList<VirtualMachineScaleSetExtensionData>? extensions,
        string password,
        string sshPublicKey,
        IDictionary<string, string> tags);

    IAsyncEnumerable<VirtualMachineScaleSetVmResource> ListVmss(Guid name);
    Async.Task<OneFuzzResultVoid> ReimageNodes(Guid scalesetId, IEnumerable<Node> nodes);
    Async.Task<OneFuzzResultVoid> DeleteNodes(Guid scalesetId, IEnumerable<Node> nodes);
}

public class VmssOperations : IVmssOperations {
    private readonly ILogTracer _log;
    private readonly ICreds _creds;
    private readonly IServiceConfig _serviceConfig;
    private readonly IMemoryCache _cache;


    public VmssOperations(ILogTracer log, IOnefuzzContext context, IMemoryCache cache) {
        _log = log.WithTag("Component", "vmss-operations");
        _creds = context.Creds;
        _serviceConfig = context.ServiceConfiguration;
        _cache = cache;
    }

    public async Async.Task<bool> DeleteVmss(Guid name, bool? forceDeletion = null) {
        var r = GetVmssResource(name);
        var result = await r.DeleteAsync(WaitUntil.Started, forceDeletion: forceDeletion);
        var raw = result.GetRawResponse();
        if (raw.IsError) {
            _log.WithHttpStatus(((HttpStatusCode)raw.Status, raw.ReasonPhrase)).Error($"Failed to delete vmss: {name:Tag:VmssName}");
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
            var scalesetResource = GetVmssResource(name);
            var patch = new VirtualMachineScaleSetPatch();
            patch.Sku.Capacity = capacity;
            try {
                _log.Info($"updating VM count {name:Tag:VmssName} - {capacity:Tag:Count}");
                _ = await scalesetResource.UpdateAsync(WaitUntil.Started, patch);
                return OneFuzzResultVoid.Ok;
            } catch (RequestFailedException ex) {
                _log.Exception(ex, $"failed to update VM counts");
                return OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_RESIZE, "vmss resize failed");
            }
        } else {
            return OneFuzzResultVoid.Error(canUpdate.ErrorV);
        }
    }


    private VirtualMachineScaleSetResource GetVmssResource(Guid name) {
        var id = VirtualMachineScaleSetResource.CreateResourceIdentifier(
            _creds.GetSubscription(),
            _creds.GetBaseResourceGroup(),
            name.ToString());
        return _creds.ArmClient.GetVirtualMachineScaleSetResource(id);
    }

    private VirtualMachineScaleSetVmResource GetVmssVmResource(Guid name, string instanceId) {
        var id = VirtualMachineScaleSetVmResource.CreateResourceIdentifier(
            _creds.GetSubscription(),
            _creds.GetBaseResourceGroup(),
            name.ToString(),
            instanceId);
        return _creds.ArmClient.GetVirtualMachineScaleSetVmResource(id);
    }

    public async Async.Task<VirtualMachineScaleSetData?> GetVmss(Guid name) {
        try {
            var res = await GetVmssResource(name).GetAsync();
            _log.Verbose($"getting vmss: {name:Tag:VmssName}");
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
            var res = GetVmssResource(name);
            var patch = new VirtualMachineScaleSetPatch() {
                VirtualMachineProfile =
                    new VirtualMachineScaleSetUpdateVmProfile() { ExtensionProfile = new VirtualMachineScaleSetExtensionProfile() }
            };

            foreach (var ext in extensions) {
                patch.VirtualMachineProfile.ExtensionProfile.Extensions.Add(ext);
            }
            try {
                _log.Info($"updating extensions of scaleset: {name:Tag:VmssName}");
                _ = await res.UpdateAsync(WaitUntil.Started, patch);
                _log.Info($"VM extensions updated: {name:Tag:VmssName}");
                return OneFuzzResultVoid.Ok;
            } catch (RequestFailedException ex) {
                _log.Exception(ex, $"failed to update scaleset extensions");
                return OneFuzzResultVoid.Error(ErrorCode.VM_UPDATE_FAILED, "vmss patch failed");
            }
        } else {
            return OneFuzzResultVoid.Error(canUpdate.ErrorV);
        }
    }

    public async Async.Task<IDictionary<Guid, string>> ListInstanceIds(Guid name) {
        _log.Verbose($"get instance IDs for scaleset {name:Tag:VmssName}");
        try {
            var results = new Dictionary<Guid, string>();
            await foreach (var instance in GetVmssResource(name).GetVirtualMachineScaleSetVms()) {
                if (instance is not null) {
                    if (Guid.TryParse(instance.Data.VmId, out var machineId)) {
                        results[machineId] = instance.Data.InstanceId;
                    } else {
                        _log.Error($"failed to convert vmId {instance.Data.VmId:Tag:VmId} to Guid in {name:Tag:VmssName}");
                    }
                }
            }
            return results;
        } catch (RequestFailedException ex) when (ex.Status == 404) {
            _log.Verbose($"scaleset does not exist {name:Tag:VmssName}");
            return new Dictionary<Guid, string>();
        }
    }

    private sealed record InstanceIdKey(Guid Scaleset, Guid VmId);
    private Task<string> GetInstanceIdForVmId(Guid scaleset, Guid vmId)
        => _cache.GetOrCreateAsync(new InstanceIdKey(scaleset, vmId), async entry => {
            var scalesetResource = GetVmssResource(scaleset);
            var vmIdString = vmId.ToString();
            string? foundInstanceId = null;
            await foreach (var vm in scalesetResource.GetVirtualMachineScaleSetVms()) {
                var vmInfo = vm.HasData ? vm : await vm.GetAsync();
                var instanceId = vmInfo.Data.InstanceId;
                if (vmInfo.Data.VmId == vmIdString) {
                    // we found the VM we are looking for
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                    foundInstanceId = instanceId;
                } else {
                    // if we find any other VMs, put them in the cache
                    if (Guid.TryParse(vmInfo.Data.VmId, out var vmId)) {
                        using var e = _cache.CreateEntry(new InstanceIdKey(scaleset, vmId));
                        _ = e.SetValue(instanceId);
                        e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                    }
                }
            }
            if (foundInstanceId is null) {
                throw new Exception($"unable to find instance ID for scaleset vm {scaleset}:{vmId}");
            } else {
                return foundInstanceId;
            }
        })!; // NULLABLE: only this method inserts InstanceIdKey so it cannot be null

    public async Async.Task<OneFuzzResult<VirtualMachineScaleSetVmResource>> GetInstanceVm(Guid name, Guid vmId) {
        _log.Info($"get instance ID for scaleset node: {name:Tag:VmssName}:{vmId:Tag:VmId}");
        var instanceId = await GetInstanceId(name, vmId);
        if (!instanceId.IsOk) {
            return instanceId.ErrorV;
        }

        var resource = GetVmssVmResource(name, instanceId.OkV);
        try {
            var response = await resource.GetAsync();
            return OneFuzzResult.Ok(response.Value);
        } catch (Exception ex) when (ex is RequestFailedException || ex is CloudException) {
            _log.Exception(ex, $"unable to find vm instance: {name:Tag:VmssName}:{vmId:Tag:VmId}");
            return OneFuzzResult<VirtualMachineScaleSetVmResource>.Error(ErrorCode.UNABLE_TO_FIND, $"unable to find vm instance: {name}:{instanceId}");
        }
    }

    public async Async.Task<OneFuzzResult<string>> GetInstanceId(Guid name, Guid vmId) {
        try {
            return OneFuzzResult.Ok(await GetInstanceIdForVmId(name, vmId));
        } catch {
            return Error.Create(ErrorCode.UNABLE_TO_FIND, $"unable to find scaleset machine: {name}:{vmId}");
        }
    }

    public async Async.Task<OneFuzzResultVoid> UpdateScaleInProtection(Scaleset scaleset, string instanceId, bool protectFromScaleIn) {
        var data = new VirtualMachineScaleSetVmData(scaleset.Region) {
            ProtectionPolicy = new VirtualMachineScaleSetVmProtectionPolicy {
                ProtectFromScaleIn = protectFromScaleIn,
                ProtectFromScaleSetActions = false,
            }
        };
        var vmCollection = GetVmssResource(scaleset.ScalesetId).GetVirtualMachineScaleSetVms();
        try {
            _ = await vmCollection.CreateOrUpdateAsync(WaitUntil.Started, instanceId, data);
            return OneFuzzResultVoid.Ok;
        } catch (RequestFailedException ex) when (ex.Status == 409 && ex.Message.StartsWith("The request failed due to conflict with a concurrent request")) {
            return OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_UPDATE, $"protection policy update is already in progress: {instanceId} in vmss {scaleset.ScalesetId}");
        } catch (RequestFailedException ex) when (ex.Status == 400 && ex.Message.Contains("The provided instanceId") && ex.Message.Contains("not an active Virtual Machine Scale Set VM instanceId.")) {
            return OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_UPDATE, $"The node with instanceId {instanceId} no longer exists in scaleset {scaleset.ScalesetId}");
        } catch (RequestFailedException ex) when (ex.Status == 400 && ex.Message.Contains("reached its limit") && ex.Message.Contains("Upgrade the VMs to the latest model")) {
            return OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_UPDATE, $"VMSS has reached model limit. Could not update scaling protection for scaleset {scaleset.ScalesetId}.");
        } catch (Exception ex) {
            _log.Exception(ex, $"unable to set protection policy on: {instanceId:Tag:InstanceId} in vmss {scaleset.ScalesetId:Tag:ScalesetId}");
            return OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_UPDATE, $"unable to set protection policy on: {instanceId} in vmss {scaleset.ScalesetId}");
        }

    }

    public async Async.Task<OneFuzzResultVoid> CreateVmss(
        Region location,
        Guid name,
        string vmSku,
        long vmCount,
        ImageReference image,
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

        _log.Info($"creating VM name: {name:Tag:VmssName} {vmSku:Tag:VmSku} {vmCount:Tag:VmCount} {image:Tag:Image} {networkId:Tag:Subnet} {spotInstance:Tag:SpotInstance}");
        var getOsResult = await image.GetOs(_cache, _creds.ArmClient, location);
        if (!getOsResult.IsOk) {
            return getOsResult.ErrorV;
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
                    ImageReference = image.ToArm(),
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
                _log.Error($"Failed to create new scaleset due to {createUpdate.GetRawResponse().ReasonPhrase:Tag:Error}");
                return OneFuzzResultVoid.Error(ErrorCode.VM_CREATE_FAILED, new[] { $"Failed to create new scaleset due to {createUpdate.GetRawResponse().ReasonPhrase}" });
            } else {
                return OneFuzzResultVoid.Ok;
            }
        } catch (Exception ex) {
            _log.Exception(ex);
            return OneFuzzResultVoid.Error(ErrorCode.VM_CREATE_FAILED, new[] { ex.Message });
        }
    }

    public IAsyncEnumerable<VirtualMachineScaleSetVmResource> ListVmss(Guid name)
        => GetVmssResource(name)
            .GetVirtualMachineScaleSetVms()
            .SelectAwait(async vm => vm.HasData ? vm : await vm.GetAsync());

    private sealed record AvailableSkusKey(Region region);
    public Async.Task<IReadOnlyList<string>> ListAvailableSkus(Region region)
        => _cache.GetOrCreateAsync<IReadOnlyList<string>>(new AvailableSkusKey(region), async entry => {
            entry = entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(10));

            var sub = _creds.GetSubscriptionResource();
            var skus = sub.GetResourceSkusAsync(filter: Query.CreateQueryFilter($"location eq {region.String}"));

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
        })!; // NULLABLE: only this method inserts AvailableSkusKey so it cannot be null

    private async Async.Task<HashSet<string>> ResolveInstanceIds(Guid scalesetId, IEnumerable<Node> nodes) {

        // only initialize this if we find a missing InstanceId
        var machineToInstanceLazy = new Lazy<Task<IDictionary<Guid, string>>>(async () => {
            var machineToInstance = await ListInstanceIds(scalesetId);
            if (!machineToInstance.Any()) {
                throw new Exception($"cannot find nodes in scaleset {scalesetId}: scaleset does not exist");
            }

            return machineToInstance;
        });

        var instanceIds = new HashSet<string>();
        foreach (var node in nodes) {
            if (node.InstanceId is not null) {
                _ = instanceIds.Add(node.InstanceId);
                continue;
            }

            var lookup = await machineToInstanceLazy.Value;
            if (lookup.TryGetValue(node.MachineId, out var foundId)) {
                _ = instanceIds.Add(foundId);
            } else {
                _log.Info($"unable to find instance ID for {scalesetId:Tag:ScalesetId} - {node.MachineId:Tag:VmId}");
            }
        }

        return instanceIds;
    }

    public async Async.Task<OneFuzzResultVoid> ReimageNodes(Guid scalesetId, IEnumerable<Node> nodes) {
        var result = await CheckCanUpdate(scalesetId);
        if (!result.IsOk) {
            return OneFuzzResultVoid.Error(result.ErrorV);
        }

        var instanceIds = await ResolveInstanceIds(scalesetId, nodes);
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
        try {
            _log.Info($"upgrading VMSS nodes - name: {scalesetId:Tag:ScalesetId} ids: {string.Join(", ", instanceIds):Tag:InstanceIds}");
            var r = await vmssResource.UpdateInstancesAsync(
                WaitUntil.Started,
                new VirtualMachineScaleSetVmInstanceRequiredIds(instanceIds));
            if (r.GetRawResponse().IsError) {
                _log.Error($"failed to start upgrade instance for scaleset {scalesetId:Tag:ScalesetId} due to {r.GetRawResponse().ReasonPhrase:Tag:Error}");
            }
        } catch (RequestFailedException ex) {
            _log.Exception(ex, $"failed to upgrade scaleset instances");
        }

        // very weird API here…
        var reqInstanceIds = new VirtualMachineScaleSetVmInstanceIds();
        foreach (var instanceId in instanceIds) {
            reqInstanceIds.InstanceIds.Add(instanceId);
        }
        try {
            _log.Info($"reimaging VMSS nodes: {scalesetId:Tag:ScalesetId} - {string.Join(", ", instanceIds):Tag:InstanceIds}");
            var r = await vmssResource.ReimageAllAsync(WaitUntil.Started, reqInstanceIds);
            if (r.GetRawResponse().IsError) {
                _log.Error($"failed to start reimage all for scaleset {scalesetId:Tag:ScalesetId} due to {r.GetRawResponse().ReasonPhrase:Tag:Error}");
            }
        } catch (RequestFailedException ex) {
            _log.Exception(ex, $"failed to reimage scaleset instances");
        }

        return OneFuzzResultVoid.Ok;
    }

    public async Async.Task<OneFuzzResultVoid> DeleteNodes(Guid scalesetId, IEnumerable<Node> nodes) {
        var result = await CheckCanUpdate(scalesetId);
        if (!result.IsOk) {
            _log.Warning($"cannot delete nodes from scaleset {scalesetId} : {result.ErrorV}");
            return OneFuzzResultVoid.Error(result.ErrorV);
        }

        var instanceIds = await ResolveInstanceIds(scalesetId, nodes);
        if (!instanceIds.Any()) {
            return OneFuzzResultVoid.Ok;
        }

        var subscription = _creds.GetSubscription();
        var resourceGroup = _creds.GetBaseResourceGroup();
        var vmssId = VirtualMachineScaleSetResource.CreateResourceIdentifier(
            subscription, resourceGroup, scalesetId.ToString());

        var computeClient = _creds.ArmClient;
        var vmssResource = computeClient.GetVirtualMachineScaleSetResource(vmssId);

        _log.Info($"deleting scaleset VMs - name: {scalesetId:Tag:ScalesetId} - {instanceIds:Tag:InstanceIds}");
        var r = await vmssResource.DeleteInstancesAsync(
            WaitUntil.Started,
            new VirtualMachineScaleSetVmInstanceRequiredIds(instanceIds));

        if (r.GetRawResponse().IsError) {
            _log.Error($"failed to start deletion of scaleset {scalesetId:Tag:ScalesetId} due to {r.GetRawResponse().ReasonPhrase:Tag:Error}");
        }
        return OneFuzzResultVoid.Ok;
    }
}
