using System.Net;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure;
using Azure.Core;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Rest.Azure;
namespace Microsoft.OneFuzz.Service;

public interface IVmssOperations {
    Async.Task<OneFuzzResultVoid> UpdateScaleInProtection(Scaleset scaleset, string instanceId, bool protectFromScaleIn);
    Async.Task<OneFuzzResult<string>> GetInstanceId(ScalesetId name, Guid vmId);
    Async.Task<OneFuzzResultVoid> UpdateExtensions(ScalesetId name, IList<VirtualMachineScaleSetExtensionData> extensions);
    Async.Task<VirtualMachineScaleSetData?> GetVmss(ScalesetId name);

    Async.Task<IReadOnlyList<string>> ListAvailableSkus(Region region);

    Async.Task<bool> DeleteVmss(ScalesetId name, bool? forceDeletion = null);

    Async.Task<IDictionary<Guid, string>> ListInstanceIds(ScalesetId name);

    Async.Task<long?> GetVmssSize(ScalesetId name);

    Async.Task<OneFuzzResultVoid> ResizeVmss(ScalesetId name, long capacity);

    Async.Task<OneFuzzResultVoid> CreateVmss(
        Region location,
        ScalesetId name,
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

    IAsyncEnumerable<VirtualMachineScaleSetVmResource> ListVmss(ScalesetId name);
    Async.Task<OneFuzzResultVoid> ReimageNodes(ScalesetId scalesetId, IEnumerable<Node> nodes);
    Async.Task<OneFuzzResultVoid> DeleteNodes(ScalesetId scalesetId, IEnumerable<Node> nodes);
}

public class VmssOperations : IVmssOperations {
    private readonly ILogger _log;
    private readonly ICreds _creds;
    private readonly IServiceConfig _serviceConfig;
    private readonly IMemoryCache _cache;


    public VmssOperations(ILogger<VmssOperations> log, IOnefuzzContext context, IMemoryCache cache) {
        _log = log;
        _log.AddTag("Component", "vmss-operations");
        _creds = context.Creds;
        _serviceConfig = context.ServiceConfiguration;
        _cache = cache;
    }

    public async Async.Task<bool> DeleteVmss(ScalesetId name, bool? forceDeletion = null) {
        var r = GetVmssResource(name);
        var result = await r.DeleteAsync(WaitUntil.Started, forceDeletion: forceDeletion);
        var raw = result.GetRawResponse();
        if (raw.IsError) {
            _log.AddHttpStatus(((HttpStatusCode)raw.Status, raw.ReasonPhrase));
            _log.LogError("Failed to delete vmss: {VmssName}", name);
            return false;
        } else {
            return true;
        }
    }

    public async Async.Task<long?> GetVmssSize(ScalesetId name) {
        var vmss = await GetVmss(name);
        if (vmss == null) {
            return null;
        }
        return vmss.Sku.Capacity;
    }

    public async Async.Task<OneFuzzResultVoid> ResizeVmss(ScalesetId name, long capacity) {
        var canUpdate = await CheckCanUpdate(name);
        if (canUpdate.IsOk) {
            var scalesetResource = GetVmssResource(name);
            var patch = new VirtualMachineScaleSetPatch();
            patch.Sku.Capacity = capacity;
            try {
                _log.LogInformation("updating VM count {VmssName} - {Count}", name, capacity);
                _ = await scalesetResource.UpdateAsync(WaitUntil.Started, patch);
                return OneFuzzResultVoid.Ok;
            } catch (RequestFailedException ex) {
                _log.LogError(ex, "failed to update VM counts");
                return OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_RESIZE, "vmss resize failed");
            }
        } else {
            return OneFuzzResultVoid.Error(canUpdate.ErrorV);
        }
    }


    private VirtualMachineScaleSetResource GetVmssResource(ScalesetId name) {
        var id = VirtualMachineScaleSetResource.CreateResourceIdentifier(
            _creds.GetSubscription(),
            _creds.GetBaseResourceGroup(),
            name.ToString());
        return _creds.ArmClient.GetVirtualMachineScaleSetResource(id);
    }

    private VirtualMachineScaleSetVmResource GetVmssVmResource(ScalesetId name, string instanceId) {
        var id = VirtualMachineScaleSetVmResource.CreateResourceIdentifier(
            _creds.GetSubscription(),
            _creds.GetBaseResourceGroup(),
            name.ToString(),
            instanceId);
        return _creds.ArmClient.GetVirtualMachineScaleSetVmResource(id);
    }

    public async Async.Task<VirtualMachineScaleSetData?> GetVmss(ScalesetId name) {
        try {
            var res = await GetVmssResource(name).GetAsync();
            _log.LogDebug("getting vmss: {VmssName}", name);
            return res.Value.Data;
        } catch (RequestFailedException ex) when (ex.Status == 404) {
            return null;
        }
    }

    public async Async.Task<OneFuzzResult<VirtualMachineScaleSetData>> CheckCanUpdate(ScalesetId name) {
        var vmss = await GetVmss(name);
        if (vmss is null) {
            return OneFuzzResult<VirtualMachineScaleSetData>.Error(ErrorCode.UNABLE_TO_UPDATE, $"vmss not found: {name}");
        }
        if (vmss.ProvisioningState == "Updating") {
            return OneFuzzResult<VirtualMachineScaleSetData>.Error(ErrorCode.UNABLE_TO_UPDATE, $"vmss is in updating state: {name}");
        }
        return OneFuzzResult<VirtualMachineScaleSetData>.Ok(vmss);
    }


    public async Async.Task<OneFuzzResultVoid> UpdateExtensions(ScalesetId name, IList<VirtualMachineScaleSetExtensionData> extensions) {
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
                _log.LogInformation("updating extensions of scaleset: {VmssName}", name);
                _ = await res.UpdateAsync(WaitUntil.Started, patch);
                _log.LogInformation("VM extensions updated: {VmssName}", name);
                return OneFuzzResultVoid.Ok;
            } catch (RequestFailedException ex) {
                _log.LogError(ex, "failed to update scaleset extensions");
                return OneFuzzResultVoid.Error(ErrorCode.VM_UPDATE_FAILED, "vmss patch failed");
            }
        } else {
            return OneFuzzResultVoid.Error(canUpdate.ErrorV);
        }
    }

    public async Async.Task<IDictionary<Guid, string>> ListInstanceIds(ScalesetId name) {
        _log.LogDebug("get instance IDs for scaleset {VmssName}", name);
        try {
            var results = new Dictionary<Guid, string>();
            await foreach (var instance in GetVmssResource(name).GetVirtualMachineScaleSetVms()) {
                if (instance is not null) {
                    if (Guid.TryParse(instance.Data.VmId, out var machineId)) {
                        results[machineId] = instance.Data.InstanceId;
                    } else {
                        _log.LogError("failed to convert vmId {VmId} to Guid in {VmssName}", instance.Data.VmId, name);
                    }
                }
            }
            return results;
        } catch (RequestFailedException ex) when (ex.Status == 404) {
            _log.LogDebug("scaleset does not exist {VmssName}", name);
            return new Dictionary<Guid, string>();
        }
    }

    private sealed record InstanceIdKey(ScalesetId Scaleset, Guid VmId);
    private Task<string> GetInstanceIdForVmId(ScalesetId scaleset, Guid vmId)
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

    public async Async.Task<OneFuzzResult<VirtualMachineScaleSetVmResource>> GetInstanceVm(ScalesetId name, Guid vmId) {
        _log.LogInformation("get instance ID for scaleset node: {VmssName}:{VmId}", name, vmId);
        var instanceId = await GetInstanceId(name, vmId);
        if (!instanceId.IsOk) {
            return instanceId.ErrorV;
        }

        var resource = GetVmssVmResource(name, instanceId.OkV);
        try {
            var response = await resource.GetAsync();
            return OneFuzzResult.Ok(response.Value);
        } catch (Exception ex) when (ex is RequestFailedException || ex is CloudException) {
            _log.LogError(ex, "unable to find vm instance: {VmssName}:{VmId}", name, vmId);
            return OneFuzzResult<VirtualMachineScaleSetVmResource>.Error(ErrorCode.UNABLE_TO_FIND, $"unable to find vm instance: {name}:{instanceId}");
        }
    }

    public async Async.Task<OneFuzzResult<string>> GetInstanceId(ScalesetId name, Guid vmId) {
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
            return OneFuzzResultVoid.Error(ErrorCode.SCALE_IN_PROTECTION_UPDATE_ALREADY_IN_PROGRESS, $"protection policy update is already in progress: {instanceId} in vmss {scaleset.ScalesetId}");
        } catch (RequestFailedException ex) when (ex.Status == 400 && ex.Message.Contains("The provided instanceId") && ex.Message.Contains("not an active Virtual Machine Scale Set VM instanceId.")) {
            return OneFuzzResultVoid.Error(ErrorCode.SCALE_IN_PROTECTION_INSTANCE_NO_LONGER_EXISTS, $"The node with instanceId {instanceId} no longer exists in scaleset {scaleset.ScalesetId}");
        } catch (RequestFailedException ex) when (ex.Status == 400 && ex.Message.Contains("reached its limit") && ex.Message.Contains("Upgrade the VMs to the latest model")) {
            return OneFuzzResultVoid.Error(ErrorCode.SCALE_IN_PROTECTION_REACHED_MODEL_LIMIT, $"VMSS has reached model limit. Could not update scaling protection for scaleset {scaleset.ScalesetId}.");
        } catch (Exception ex) {
            _log.LogError(ex, "unable to set protection policy on: {InstanceId} in vmss {ScalesetId}", instanceId, scaleset.ScalesetId);
            return OneFuzzResultVoid.Error(ErrorCode.SCALE_IN_PROTECTION_UNEXPECTED_ERROR, $"unable to set protection policy on: {instanceId} in vmss {scaleset.ScalesetId}");
        }

    }

    public async Async.Task<OneFuzzResultVoid> CreateVmss(
        Region location,
        ScalesetId name,
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

        _log.LogInformation("creating VM name: {VmssName} {VmSku} {VmCount} {Image} {Subnet} {SpotInstance}", name, vmSku, vmCount, image, networkId, spotInstance);
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
                _log.LogError("Failed to create new scaleset due to {Error}", createUpdate.GetRawResponse().ReasonPhrase);
                return OneFuzzResultVoid.Error(ErrorCode.VM_CREATE_FAILED, new[] { $"Failed to create new scaleset due to {createUpdate.GetRawResponse().ReasonPhrase}" });
            } else {
                return OneFuzzResultVoid.Ok;
            }
        } catch (Exception ex) {
            _log.LogError(ex, "CreateVm");
            return OneFuzzResultVoid.Error(ErrorCode.VM_CREATE_FAILED, new[] { ex.Message });
        }
    }

    public IAsyncEnumerable<VirtualMachineScaleSetVmResource> ListVmss(ScalesetId name)
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

    private async Async.Task<HashSet<string>> ResolveInstanceIds(ScalesetId scalesetId, IEnumerable<Node> nodes) {

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
                _log.LogInformation("unable to find instance ID for {ScalesetId} - {VmId}", scalesetId, node.MachineId);
            }
        }

        return instanceIds;
    }

    public async Async.Task<OneFuzzResultVoid> ReimageNodes(ScalesetId scalesetId, IEnumerable<Node> nodes) {
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
            _log.LogInformation("upgrading VMSS nodes - name: {ScalesetId} ids: {InstanceIds}", scalesetId, string.Join(", ", instanceIds));
            var r = await vmssResource.UpdateInstancesAsync(
                WaitUntil.Started,
                new VirtualMachineScaleSetVmInstanceRequiredIds(instanceIds));
            if (r.GetRawResponse().IsError) {
                _log.LogError("failed to start upgrade instance for scaleset {ScalesetId} due to {Error}", scalesetId, r.GetRawResponse().ReasonPhrase);
            }
        } catch (RequestFailedException ex) {
            _log.LogError(ex, "failed to upgrade scaleset instances");
        }

        // very weird API here…
        var reqInstanceIds = new VirtualMachineScaleSetVmInstanceIds();
        foreach (var instanceId in instanceIds) {
            reqInstanceIds.InstanceIds.Add(instanceId);
        }
        try {
            _log.LogInformation("reimaging VMSS nodes: {ScalesetId} - {InstanceIds}", scalesetId, string.Join(", ", instanceIds));
            var r = await vmssResource.ReimageAllAsync(WaitUntil.Started, reqInstanceIds);
            if (r.GetRawResponse().IsError) {
                _log.LogError("failed to start reimage all for scaleset {ScalesetId} due to {Error}", scalesetId, r.GetRawResponse().ReasonPhrase);
            }
        } catch (RequestFailedException ex) {
            _log.LogError(ex, "failed to reimage scaleset instances");
        }

        return OneFuzzResultVoid.Ok;
    }

    public async Async.Task<OneFuzzResultVoid> DeleteNodes(ScalesetId scalesetId, IEnumerable<Node> nodes) {
        var result = await CheckCanUpdate(scalesetId);
        if (!result.IsOk) {
            _log.LogWarning("cannot delete nodes from scaleset {scalesetId} : {error}", scalesetId, result.ErrorV);
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

        _log.LogInformation("deleting scaleset VMs - name: {ScalesetId} - {InstanceIds}", scalesetId, instanceIds);
        var r = await vmssResource.DeleteInstancesAsync(
            WaitUntil.Started,
            new VirtualMachineScaleSetVmInstanceRequiredIds(instanceIds));

        if (r.GetRawResponse().IsError) {
            _log.LogError("failed to start deletion of scaleset {ScalesetId} due to {Error}", scalesetId, r.GetRawResponse().ReasonPhrase);
        }
        return OneFuzzResultVoid.Ok;
    }
}
