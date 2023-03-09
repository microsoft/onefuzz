﻿using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;

namespace Microsoft.OneFuzz.Service;

public interface IVmOperations {
    Async.Task<bool> IsDeleted(Vm vm);

    Async.Task<bool> HasComponents(string name);

    Task<VirtualMachineData?> GetVm(string name);

    Task<VirtualMachineData?> GetVmWithInstanceView(string name);

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

    public async Task<VirtualMachineData?> GetVm(string name) {
        try {
            var result = await _context.Creds.GetResourceGroupResource().GetVirtualMachineAsync(name);
            if (result is null || !result.Value.HasData) {
                return null;
            }

            return result.Value.Data;
        } catch (RequestFailedException) {
            // TODO: we shouldn't hide this error, only if it doesn't exist
            return null;
        }
    }

    public async Task<VirtualMachineData?> GetVmWithInstanceView(string name) {
        try {
            var result = await _context.Creds.GetResourceGroupResource().GetVirtualMachineAsync(name, InstanceViewTypes.InstanceView);
            if (result is null || !result.Value.HasData) {
                return null;
            }

            return result.Value.Data;
        } catch (RequestFailedException) {
            // TODO: we shouldn't hide this error, only if it doesn't exist
            return null;
        }
    }

    public async Async.Task<bool> Delete(Vm vm) {
        return await DeleteVmComponents(vm.Name, vm.Nsg);
    }

    public async Async.Task<bool> DeleteVmComponents(string name, Nsg? nsg) {
        var resourceGroup = _context.Creds.GetBaseResourceGroup();
        _logTracer.Info($"deleting vm components {resourceGroup:Tag:ResourceGroup}:{name:Tag:VmName}");
        if (await GetVm(name) != null) {
            _logTracer.Info($"deleting vm {resourceGroup:Tag:ResourceGroup}:{name:Tag:VmName}");
            await DeleteVm(name);
            return false;
        }

        var nic = await _context.IpOperations.GetPublicNic(resourceGroup, name);
        if (nic != null) {
            _logTracer.Info($"deleting nic {resourceGroup:Tag:ResourceGroup}:{name:Tag:VmName}");
            if (nic.Data.NetworkSecurityGroup != null && nsg != null) {
                _ = await _context.NsgOperations.DissociateNic(nsg, nic);
                return false;
            }
            await _context.IpOperations.DeleteNic(resourceGroup, name);
            return false;
        }

        if (await _context.IpOperations.GetIp(resourceGroup, name) != null) {
            _logTracer.Info($"deleting ip {resourceGroup:Tag:ResourceGroup}:{name:Tag:VmName}");
            await _context.IpOperations.DeleteIp(resourceGroup, name);
            return false;
        }

        var disks = _context.DiskOperations.ListDisks(resourceGroup)
            .ToAsyncEnumerable()
            .Where(disk => disk.Data.Name.StartsWith(name, StringComparison.Ordinal));

        if (await disks.AnyAsync()) {
            await foreach (var disk in disks) {
                _logTracer.Info($"deleting disk {resourceGroup:Tag:ResourceGroup}:{disk?.Data.Name:Tag:DiskName}");
                _ = await _context.DiskOperations.DeleteDisk(resourceGroup, disk?.Data.Name!);
            }
            return false;
        }

        return true;
    }

    public async System.Threading.Tasks.Task DeleteVm(string name) {
        _logTracer.Info($"deleting vm: {_context.Creds.GetBaseResourceGroup():Tag:ResourceGroup} {name:Tag:VmName}");

        var r = await _context.Creds.GetResourceGroupResource()
            .GetVirtualMachineAsync(name).Result.Value
            .DeleteAsync(WaitUntil.Started);
        if (r.GetRawResponse().IsError) {
            _logTracer.Error($"failed to start deletion of vm {name:Tag:VmName} due to {r.GetRawResponse().ReasonPhrase:Tag:Error}");
        }
        return;
    }

    public async Task<OneFuzzResult<bool>> AddExtensions(Vm vm, Dictionary<string, VirtualMachineExtensionData> extensions) {
        var statuses = new List<(string extName, string state)>();
        var toCreate = new List<KeyValuePair<string, VirtualMachineExtensionData>>();
        foreach (var extensionConfig in extensions) {
            var extensionName = extensionConfig.Key;
            var extensionData = extensionConfig.Value;

            var extension = await GetExtension(vm.Name, extensionName);
            if (extension != null) {
                statuses.Add((extensionName, extension.ProvisioningState));
            } else {
                toCreate.Add(extensionConfig);
            }
        }

        if (toCreate.Any()) {
            foreach (var config in toCreate) {
                await CreateExtension(vm.Name, config.Key, config.Value);
            }
        } else {
            if (statuses.All(s => s.state == "Succeeded")) {
                return OneFuzzResult.Ok(true);
            } else if (statuses.Any(s => s.state == "Failed")) {
                var errors = await GetExtensionErrors(vm.Name, statuses.Where(s => s.state == "Failed").Select(s => s.extName));
                // return OneFuzzResult<bool>.Error(ErrorCode.VM_CREATE_FAILED, "failed to launch extension(s): " + errors);
                return OneFuzzResult.Ok(true);
            }
        }

        return OneFuzzResult.Ok(false);
    }

    public async Task<OneFuzzResultVoid> Create(Vm vm) {
        if (await GetVm(vm.Name) != null) {
            return OneFuzzResultVoid.Ok;
        }

        _logTracer.Info($"vm creating: {vm.Name:Tag:VmName}");

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
            // var instanceView = await vm.Value.GetVirtualMachineExtensionAsync(extensionName, expand: "InstanceView");
            return (await vm.Value.GetVirtualMachineExtensionAsync(extensionName, expand: "instanceView")).Value.Data;
        } catch (RequestFailedException ex) {
            _logTracer.Info($"extension does not exist {ex.Message:Tag:Error}");
            return null;
        }
    }

    private ResourceIdentifier GetVirtualMachineIdentifier(string vmName)
        => VirtualMachineResource.CreateResourceIdentifier(
            _context.Creds.GetSubscription(),
            _context.Creds.GetBaseResourceGroup(),
            vmName);

    public async Async.Task CreateExtension(string vmName, string extensionName, VirtualMachineExtensionData extension) {
        _logTracer.Info($"creating extension: {_context.Creds.GetBaseResourceGroup():Tag:ResourceGroup} - {vmName:Tag:VmName} - {extensionName:Tag:ExtensionName}");

        var vm = _context.Creds.ArmClient.GetVirtualMachineResource(GetVirtualMachineIdentifier(vmName));
        try {
            _ = await vm.GetVirtualMachineExtensions().CreateOrUpdateAsync(
                WaitUntil.Started,
                extensionName,
                extension);
        } catch (RequestFailedException ex) when
            (ex.Status == 409 &&
                (ex.Message.Contains("VM is marked for deletion") || ex.Message.Contains("The request failed due to conflict with a concurrent request."))) {
            _logTracer.Info($"Tried to create {extensionName:Tag:ExtensionName} for {vmName:Tag:VmName} but failed due to {ex.Message:Tag:Error}");
        }
        return;
    }

    public async Async.Task<string> GetExtensionErrors(string vmName, IEnumerable<string> extensionNames) {
        var vmResource = _context.Creds.ArmClient.GetVirtualMachineResource(GetVirtualMachineIdentifier(vmName));
        var vmData = await vmResource.GetAsync(InstanceViewTypes.InstanceView);

        var result = new StringBuilder();
        foreach (var extensionName in extensionNames) {
            result.Append($"Errors for extension '{extensionName}':");
            var extensionData = vmData.Value.Data.InstanceView.Extensions.FirstOrDefault(ext => ext.Name == extensionName);
            if (extensionData is null) {
                result.Append($" (cannot get errors - extension was not found on target VM)\n");
            } else {
                result.Append('\n');
                foreach (var status in extensionData.Statuses) {
                    result.Append($"{status.Time}:{status.Level}: {status.Code} ({status.DisplayStatus}) - {status.Message}\n");
                }
            }
        }

        return result.ToString();
    }

    async Task<OneFuzzResultVoid> CreateVm(
        string name,
        Region location,
        string vmSku,
        string image,
        string password,
        string sshPublicKey,
        Nsg? nsg,
        IDictionary<string, string>? tags
    ) {
        var resourceGroup = _context.Creds.GetBaseResourceGroup();
        _logTracer.Info($"creating vm {resourceGroup:Tag:ResourceGroup} - {location:Tag:Location} {name:Tag:VmName}");

        var nic = await _context.IpOperations.GetPublicNic(resourceGroup, name);
        if (nic == null) {
            var result = await _context.IpOperations.CreatePublicNic(resourceGroup, name, location, nsg);
            if (!result.IsOk) {
                return result;
            }

            _logTracer.Info($"waiting on nic creation for {name:Tag:VmName}");
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
                        _logTracer.Warning($"Failed to add tag {kvp.Key:Tag:Key}:{kvp.Value:Tag:Value} to vm {name:Tag:VmName}");
                    }
                });
        }

        try {
            _ = await _context.Creds.GetResourceGroupResource().GetVirtualMachines().CreateOrUpdateAsync(
                WaitUntil.Started,
                name,
                vmParams);
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
