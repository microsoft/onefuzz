using Azure;
using Azure.ResourceManager.Compute;


namespace Microsoft.OneFuzz.Service;

public interface IVmOperations {
    Async.Task<bool> IsDeleted(Vm vm);

    Async.Task<bool> HasComponents(string name);

    Async.Task<VirtualMachineResource?> GetVm(string name);

    Async.Task<bool> Delete(Vm vm);

}

public class VmOperations : IVmOperations {
    private ILogTracer _logTracer;

    private ICreds _creds;

    private IIpOperations _ipOperations;

    private IDiskOperations _diskOperations;

    private INsgOperations _nsgOperations;

    public VmOperations(ILogTracer log, ICreds creds, IIpOperations ipOperations, IDiskOperations diskOperations, INsgOperations nsgOperations) {
        _logTracer = log;
        _creds = creds;
        _ipOperations = ipOperations;
        _diskOperations = diskOperations;
        _nsgOperations = nsgOperations;
    }
    public async Async.Task<bool> IsDeleted(Vm vm) {
        return !(await HasComponents(vm.Name));
    }

    public async Async.Task<bool> HasComponents(string name) {
        var resourceGroup = _creds.GetBaseResourceGroup();
        if (await GetVm(name) != null) {
            return true;
        }

        if (await _ipOperations.GetPublicNic(resourceGroup, name) != null) {
            return true;
        }

        if (await _ipOperations.GetIp(resourceGroup, name) != null) {
            return true;
        }

        var disks = await _diskOperations.ListDisks(resourceGroup)
            .ToAsyncEnumerable()
            .Where(disk => disk.Data.Name.StartsWith(name))
            .AnyAsync();

        if (disks) {
            return true;
        }

        return false;
    }

    public async Async.Task<VirtualMachineResource?> GetVm(string name) {
        return await _creds.GetResourceGroupResource().GetVirtualMachineAsync(name);
    }

    public async Async.Task<bool> Delete(Vm vm) {
        return await DeleteVmComponents(vm.Name, vm.Nsg);
    }

    public async Async.Task<bool> DeleteVmComponents(string name, Nsg? nsg) {
        var resourceGroup = _creds.GetBaseResourceGroup();
        _logTracer.Info($"deleting vm components {resourceGroup}:{name}");
        if (GetVm(name) != null) {
            _logTracer.Info($"deleting vm {resourceGroup}:{name}");
            await DeleteVm(name);
            return false;
        }

        var nic = await _ipOperations.GetPublicNic(resourceGroup, name);
        if (nic != null) {
            _logTracer.Info($"deleting nic {resourceGroup}:{name}");
            if (nic.Data.NetworkSecurityGroup != null && nsg != null) {
                await _nsgOperations.DissociateNic(nsg, nic);
                return false;
            }
            await _ipOperations.DeleteNic(resourceGroup, name);
            return false;
        }

        if (await _ipOperations.GetIp(resourceGroup, name) != null) {
            _logTracer.Info($"deleting ip {resourceGroup}:{name}");
            await _ipOperations.DeleteIp(resourceGroup, name);
            return false;
        }

        var disks = _diskOperations.ListDisks(resourceGroup)
            .ToAsyncEnumerable()
            .Where(disk => disk.Data.Name.StartsWith(name));

        if (await disks.AnyAsync()) {
            await foreach (var disk in disks) {
                _logTracer.Info($"deleting disk {resourceGroup}:{disk?.Data.Name}");
                await _diskOperations.DeleteDisk(resourceGroup, disk?.Data.Name!);
            }
            return false;
        }

        return true;
    }

    public async System.Threading.Tasks.Task DeleteVm(string name) {
        _logTracer.Info($"deleting vm: {_creds.GetBaseResourceGroup()} {name}");
        await _creds.GetResourceGroupResource()
            .GetVirtualMachineAsync(name).Result.Value
            .DeleteAsync(WaitUntil.Started);
    }
}
