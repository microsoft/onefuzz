using System.Threading.Tasks;
using Azure;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Newtonsoft.Json;

namespace Microsoft.OneFuzz.Service;

public interface IVmOperations {
    Async.Task<bool> IsDeleted(Vm vm);

    Async.Task<bool> HasComponents(string name);

    Async.Task<VirtualMachineResource?> GetVm(string name);

    Async.Task<bool> Delete(Vm vm);

    Async.Task<OneFuzzResultVoid> Create(Vm vm);

    Async.Task<VirtualMachineExtensionData?> GetExtension(string vmName, string extensionName);

    Async.Task<OneFuzzResult<bool>> AddExtensions(Vm vm, Dictionary<string, VirtualMachineExtensionData> extensions);

    Async.Task CreateExtension(string vmName, string extensionName, VirtualMachineExtensionData extension);

}

public class VmOperations : IVmOperations {
    private ILogTracer _logTracer;
    private IOnefuzzContext _context;

    public VmOperations(ILogTracer log, IOnefuzzContext context) {
        _logTracer = log;
        _context = context;
    }

    public async Async.Task<bool> IsDeleted(Vm vm) {
        return !(await HasComponents(vm.Name));
    }

    public async Async.Task<bool> HasComponents(string name) {
        var resourceGroup = _context.Creds.GetBaseResourceGroup();
        if (await GetVm(name) != null) {
            return true;
        }

        if (await _context.IpOperations.GetPublicNic(resourceGroup, name) != null) {
            return true;
        }

        if (await _context.IpOperations.GetIp(resourceGroup, name) != null) {
            return true;
        }

        var disks = await _context.DiskOperations.ListDisks(resourceGroup)
            .ToAsyncEnumerable()
            .Where(disk => disk.Data.Name.StartsWith(name, StringComparison.Ordinal))
            .AnyAsync();

        if (disks) {
            return true;
        }

        return false;
    }

    public async Async.Task<VirtualMachineResource?> GetVm(string name) {
        // _logTracer.Debug($"getting vm: {name}");
        try {
            return await _context.Creds.GetResourceGroupResource().GetVirtualMachineAsync(name);
        } catch (RequestFailedException) {
            // _logTracer.Debug($"vm does not exist {ex});
            return null;
        }
    }

    public async Async.Task<bool> Delete(Vm vm) {
        return await DeleteVmComponents(vm.Name, vm.Nsg);
    }

    public async Async.Task<bool> DeleteVmComponents(string name, Nsg? nsg) {
        var resourceGroup = _context.Creds.GetBaseResourceGroup();
        _logTracer.Info($"deleting vm components {resourceGroup}:{name}");
        if (await GetVm(name) != null) {
            _logTracer.Info($"deleting vm {resourceGroup}:{name}");
            await DeleteVm(name);
            return false;
        }

        var nic = await _context.IpOperations.GetPublicNic(resourceGroup, name);
        if (nic != null) {
            _logTracer.Info($"deleting nic {resourceGroup}:{name}");
            if (nic.Data.NetworkSecurityGroup != null && nsg != null) {
                await _context.NsgOperations.DissociateNic((Nsg)nsg, nic);
                return false;
            }
            await _context.IpOperations.DeleteNic(resourceGroup, name);
            return false;
        }

        if (await _context.IpOperations.GetIp(resourceGroup, name) != null) {
            _logTracer.Info($"deleting ip {resourceGroup}:{name}");
            await _context.IpOperations.DeleteIp(resourceGroup, name);
            return false;
        }

        var disks = _context.DiskOperations.ListDisks(resourceGroup)
            .ToAsyncEnumerable()
            .Where(disk => disk.Data.Name.StartsWith(name, StringComparison.Ordinal));

        if (await disks.AnyAsync()) {
            await foreach (var disk in disks) {
                _logTracer.Info($"deleting disk {resourceGroup}:{disk?.Data.Name}");
                await _context.DiskOperations.DeleteDisk(resourceGroup, disk?.Data.Name!);
            }
            return false;
        }

        return true;
    }

    public async System.Threading.Tasks.Task DeleteVm(string name) {
        _logTracer.Info($"deleting vm: {_context.Creds.GetBaseResourceGroup()} {name}");

        await _context.Creds.GetResourceGroupResource()
            .GetVirtualMachineAsync(name).Result.Value
            .DeleteAsync(WaitUntil.Started);
    }


    public async Task<OneFuzzResult<bool>> AddExtensions(Vm vm, Dictionary<string, VirtualMachineExtensionData> extensions) {
        var status = new List<string>();
        var toCreate = new List<KeyValuePair<string, VirtualMachineExtensionData>>();
        foreach (var extensionConfig in extensions) {
            var extensionName = extensionConfig.Key;
            var extensionData = extensionConfig.Value;

            var extension = await GetExtension(vm.Name, extensionName);
            if (extension != null) {
                _logTracer.Info(
                    $"vm extension state: {vm.Name} - {extensionName} - {extension.ProvisioningState}"
                );
                status.Add(extension.ProvisioningState);
            } else {
                toCreate.Add(extensionConfig);
            }
        }

        if (toCreate.Any()) {
            foreach (var config in toCreate) {
                await CreateExtension(vm.Name, config.Key, config.Value);
            }
        } else {
            if (status.All(s => string.Equals(s, "Succeeded", StringComparison.Ordinal))) {
                return OneFuzzResult<bool>.Ok(true);
            } else if (status.Any(s => string.Equals(s, "Failed", StringComparison.Ordinal))) {
                return OneFuzzResult<bool>.Error(
                    ErrorCode.VM_CREATE_FAILED,
                    "failed to launch extension"
                );
            } else if (!(status.Contains("Creating") || status.Contains("Updating"))) {
                _logTracer.Error($"vm agent - unknown state {vm.Name}: {JsonConvert.SerializeObject(status)}");
            }
        }

        return OneFuzzResult<bool>.Ok(false);
    }

    public async Task<OneFuzzResultVoid> Create(Vm vm) {
        if (await GetVm(vm.Name) != null) {
            return OneFuzzResultVoid.Ok;
        }

        _logTracer.Info($"vm creating: {vm.Name}");

        return await CreateVm(
            vm.Name,
            vm.Region,
            vm.Sku,
            vm.Image,
            vm.Auth.Password,
            vm.Auth.PublicKey,
            vm.Nsg,
            vm.Tags
        );
    }

    public async Task<VirtualMachineExtensionData?> GetExtension(string vmName, string extensionName) {
        // _logTracer.Debug($"getting extension: {resourceGroup}:{vmName}:{extensionName}");
        try {
            var vm = await _context.Creds.GetResourceGroupResource().GetVirtualMachineAsync(
                vmName
            );

            return (await vm.Value.GetVirtualMachineExtensionAsync(extensionName)).Value.Data;
        } catch (RequestFailedException ex) {
            _logTracer.Info($"extension does not exist {ex}");
            return null;
        }
    }

    public async Async.Task CreateExtension(string vmName, string extensionName, VirtualMachineExtensionData extension) {
        _logTracer.Info($"creating extension: {_context.Creds.GetBaseResourceGroup()}:{vmName}:{extensionName}");
        var vm = await _context.Creds.GetResourceGroupResource().GetVirtualMachineAsync(vmName);

        await vm.Value.GetVirtualMachineExtensions().CreateOrUpdateAsync(
            WaitUntil.Started,
            extensionName,
            extension
        );
        return;
    }

    async Task<OneFuzzResultVoid> CreateVm(
        string name,
        string location,
        string vmSku,
        string image,
        string password,
        string sshPublicKey,
        Nsg? nsg,
        IDictionary<string, string>? tags
    ) {
        var resourceGroup = _context.Creds.GetBaseResourceGroup();
        _logTracer.Info($"creating vm {resourceGroup}:{location}:{name}");

        var nic = await _context.IpOperations.GetPublicNic(resourceGroup, name);
        if (nic == null) {
            var result = await _context.IpOperations.CreatePublicNic(resourceGroup, name, location, nsg);
            if (!result.IsOk) {
                return result;
            }

            _logTracer.Info("waiting on nic creation");
            return OneFuzzResultVoid.Ok;
        }

        // when public nic is created, VNET must exist at that point
        // this is logic of get_public_nic function

        if (nsg != null) {
            var result = await _context.NsgOperations.AssociateNic(nsg, nic);
            if (!result.IsOk) {
                return result;
            }
        }

        var vmParams = new VirtualMachineData(location) {
            OSProfile = new OSProfile {
                ComputerName = "node",
                AdminUsername = "onefuzz",
            },
            HardwareProfile = new HardwareProfile {
                VmSize = vmSku,
            },
            StorageProfile = new StorageProfile {
                ImageReference = GenerateImageReference(image),
            },
            NetworkProfile = new NetworkProfile(),
        };

        vmParams.NetworkProfile.NetworkInterfaces.Add(new NetworkInterfaceReference { Id = nic.Id });

        var imageOs = await _context.ImageOperations.GetOs(location, image);
        if (!imageOs.IsOk) {
            return OneFuzzResultVoid.Error(imageOs.ErrorV);
        }

        switch (imageOs.OkV) {
            case Os.Windows: {
                    vmParams.OSProfile.AdminPassword = password;
                    break;
                }
            case Os.Linux: {
                    vmParams.OSProfile.LinuxConfiguration = new LinuxConfiguration {
                        DisablePasswordAuthentication = true,
                    };
                    vmParams.OSProfile.LinuxConfiguration.SshPublicKeys.Add(
                        new SshPublicKeyInfo {
                            Path = "/home/onefuzz/.ssh/authorized_keys",
                            KeyData = sshPublicKey
                        }
                    );
                    break;
                }
            default: throw new NotSupportedException($"No support for OS type: {imageOs.OkV}");
        }

        var onefuzzOwner = _context.ServiceConfiguration.OneFuzzOwner;
        if (!string.IsNullOrEmpty(onefuzzOwner)) {
            vmParams.Tags.Add("OWNER", onefuzzOwner);
        } else {
            tags?.ToList()
                .ForEach(kvp => {
                    if (!vmParams.Tags.TryAdd(kvp.Key, kvp.Value)) {
                        _logTracer.Warning($"Failed to add tag {kvp.Key}:{kvp.Value} to vm {name}");
                    }
                });
        }

        try {
            await _context.Creds.GetResourceGroupResource().GetVirtualMachines().CreateOrUpdateAsync(
                WaitUntil.Started,
                name,
                vmParams
            );
        } catch (RequestFailedException ex) {
            if (ex.ErrorCode == "ResourceNotFound" && ex.Message.Contains("The request failed due to conflict with a concurrent request")) {
                // _logTracer.Debug($"create VM had conflicts with concurrent request, ignoring {ex.ToString()}");
                return OneFuzzResultVoid.Ok;
            }

            return OneFuzzResultVoid.Error(
                ErrorCode.VM_CREATE_FAILED,
                ex.ToString()
            );
        }

        return OneFuzzResultVoid.Ok;
    }

    private static ImageReference GenerateImageReference(string image) {
        var imageRef = new ImageReference();

        if (image.StartsWith("/", StringComparison.Ordinal)) {
            imageRef.Id = image;
        } else {
            var imageVal = image.Split(":", 4);
            imageRef.Publisher = imageVal[0];
            imageRef.Offer = imageVal[1];
            imageRef.Sku = imageVal[2];
            imageRef.Version = imageVal[3];
        }

        return imageRef;
    }
}
