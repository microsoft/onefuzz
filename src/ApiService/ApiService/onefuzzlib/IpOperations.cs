using System.Threading.Tasks;
using Azure;
using Azure.ResourceManager.Network;

namespace Microsoft.OneFuzz.Service;

public interface IIpOperations {
    public Async.Task<NetworkInterfaceResource> GetPublicNic(string resourceGroup, string name);

    public Async.Task<PublicIPAddressResource> GetIp(string resourceGroup, string name);

    public Async.Task DeleteNic(string resourceGroup, string name);

    public Async.Task DeleteIp(string resourceGroup, string name);

    public Async.Task<string?> GetScalesetInstanceIp(Guid scalesetId, Guid machineId);
}

public class IpOperations : IIpOperations {
    private ILogTracer _logTracer;

    private IOnefuzzContext _context;

    public IpOperations(ILogTracer log, IOnefuzzContext context) {
        _logTracer = log;
        _context = context;
    }

    public async Async.Task<NetworkInterfaceResource> GetPublicNic(string resourceGroup, string name) {
        _logTracer.Info($"getting nic: {resourceGroup} {name}");
        return await _context.Creds.GetResourceGroupResource().GetNetworkInterfaceAsync(name);
    }

    public async Async.Task<PublicIPAddressResource> GetIp(string resourceGroup, string name) {
        _logTracer.Info($"getting ip {resourceGroup}:{name}");
        return await _context.Creds.GetResourceGroupResource().GetPublicIPAddressAsync(name);
    }

    public async System.Threading.Tasks.Task DeleteNic(string resourceGroup, string name) {
        _logTracer.Info($"deleting nic {resourceGroup}:{name}");
        await _context.Creds.GetResourceGroupResource().GetNetworkInterfaceAsync(name).Result.Value.DeleteAsync(WaitUntil.Started);
    }

    public async System.Threading.Tasks.Task DeleteIp(string resourceGroup, string name) {
        _logTracer.Info($"deleting ip {resourceGroup}:{name}");
        await _context.Creds.GetResourceGroupResource().GetPublicIPAddressAsync(name).Result.Value.DeleteAsync(WaitUntil.Started);
    }

    public async Task<string?> GetScalesetInstanceIp(Guid scalesetId, Guid machineId) {
        var instance = await _context.VmssOperations.GetInstanceId(scalesetId, machineId);
        if (!instance.IsOk) {
            return null;
        }

        // var resourceGroup = _context.Creds.GetBaseResourceGroup();
        // var id = VirtualMachineScaleSetResource.CreateResourceIdentifier(_context.Creds.GetSubscription(), resourceGroup, scalesetId.ToString());
        // var vmss =  _context.Creds.ArmClient.GetVirtualMachineScaleSetResource(id);
        // var vm = await vmss.GetVirtualMachineScaleSetVmAsync(machineId.ToString());

        // todo: find how to get the private ip

        return "";
    }
}
