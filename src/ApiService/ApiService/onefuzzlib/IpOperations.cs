using System.Threading.Tasks;
using Azure;
using Azure.ResourceManager.Network;

namespace Microsoft.OneFuzz.Service;

public interface IIpOperations {
    public Async.Task<NetworkInterfaceResource> GetPublicNic(string resourceGroup, string name);

    public Async.Task<OneFuzzResultVoid> CreatePublicNic(string resourceGroup, string name, string region, Nsg? nsg);

    public Async.Task<string?> GetPublicIp(string resourceId);

    public Async.Task<PublicIPAddressResource?> GetIp(string resourceGroup, string name);

    public Async.Task DeleteNic(string resourceGroup, string name);

    public Async.Task DeleteIp(string resourceGroup, string name);

    public Async.Task<PublicIPAddressResource> CreateIp(string resourceGroup, string name, string region);
}

public class IpOperations : IIpOperations {
    private ILogTracer _logTracer;

    private ICreds _creds;

    private IOnefuzzContext _context;

    public IpOperations(ILogTracer log, ICreds creds, IOnefuzzContext context) {
        _logTracer = log;
        _creds = creds;
        _context = context;
    }

    public async Async.Task<NetworkInterfaceResource> GetPublicNic(string resourceGroup, string name) {
        _logTracer.Info($"getting nic: {resourceGroup} {name}");
        return await _creds.GetResourceGroupResource().GetNetworkInterfaceAsync(name);
    }

    public async Async.Task<PublicIPAddressResource?> GetIp(string resourceGroup, string name) {
        _logTracer.Info($"getting ip {resourceGroup}:{name}");

        try{
            return await _creds.GetResourceGroupResource().GetPublicIPAddressAsync(name);
        }
        catch (RequestFailedException) {
            return null;
        }
    }

    public async System.Threading.Tasks.Task DeleteNic(string resourceGroup, string name) {
        _logTracer.Info($"deleting nic {resourceGroup}:{name}");
        await _creds.GetResourceGroupResource().GetNetworkInterfaceAsync(name).Result.Value.DeleteAsync(WaitUntil.Started);
    }

    public async System.Threading.Tasks.Task DeleteIp(string resourceGroup, string name) {
        _logTracer.Info($"deleting ip {resourceGroup}:{name}");
        await _creds.GetResourceGroupResource().GetPublicIPAddressAsync(name).Result.Value.DeleteAsync(WaitUntil.Started);
    }

    public async Task<string?> GetPublicIp(string resourceId) {
        // TODO: Parts of this function seem redundant, but I'm mirroring
        // the python code exactly. We should revisit this.
        _logTracer.Info($"getting ip for {resourceId}");
        var resource = _creds.ParseResourceId(resourceId);
        var networkInterfaces = await _creds.GetResourceGroupResource().GetNetworkInterfaceAsync(
            resource.Data.Name
        );
        var publicIp = (await networkInterfaces.Value.GetNetworkInterfaceIPConfigurations().FirstAsync())
                    .Data.PublicIPAddress;
        resource = _creds.ParseResourceId(publicIp.Id);
        var publicIpResource = await _creds.GetResourceGroupResource().GetPublicIPAddressAsync(
            resource.Data.Name
        );

        if (publicIpResource == null) {
            return null;
        }
        else {
            return publicIpResource.Value.Data.IPAddress;
        }
    }

    public async Task<OneFuzzResultVoid> CreatePublicNic(string resourceGroup, string name, string region, Nsg? nsg) {
        _logTracer.Info($"creating nic for {resourceGroup}:{name} in {region}");

        var network = await Network.Init(region, _context);
        var subnetId = await network.GetId();

        if (string.IsNullOrEmpty(subnetId)) {
            await network.Create();
            return OneFuzzResultVoid.Ok;
        }

        if (nsg != null) {
            var subnet = await network.GetSubnet();
            if (subnet != null && subnet.Data.NetworkSecurityGroup == null) {
                var vnet = await network.GetVnet();
                var result = await _context.NsgOperations.AssociateSubnet(nsg.Name, vnet!, subnet);
                if (result != null) {
                    return OneFuzzResultVoid.Error(result);
                }
                return OneFuzzResultVoid.Ok;
            }
        }

        var ip = await GetIp(resourceGroup, name);
        if (ip != null) {
            await CreateIp(resourceGroup, name, region);
            return OneFuzzResultVoid.Ok;
        }

        var networkInterface = new NetworkInterfaceData
        {
            Location = region,
        };

        networkInterface.IPConfigurations.Add(new NetworkInterfaceIPConfigurationData
            {
                Name = "myIPConfig",
                PublicIPAddress = ip?.Data,
                Subnet = new SubnetData
                {
                    Id = subnetId
                }
            }
        );

        var onefuzzOwner = _context.ServiceConfiguration.OneFuzzOwner;
        if (!string.IsNullOrEmpty(onefuzzOwner)) {
            if (!networkInterface.Tags.TryAdd("OWNER", onefuzzOwner)) {
                _logTracer.Warning($"Failed to add tag 'OWNER':{onefuzzOwner} to nic {resourceGroup}:{name}");
            }
        }

        try {
            await _creds.GetResourceGroupResource().GetNetworkInterfaces().CreateOrUpdateAsync(
                WaitUntil.Started,
                name,
                networkInterface
            );
        }
        catch (RequestFailedException ex) {
            if (!ex.ToString().Contains("RetryableError")) {
                return OneFuzzResultVoid.Error(
                    ErrorCode.VM_CREATE_FAILED,
                    $"unable to create nic: {ex}"
                );
            }
        }

        return OneFuzzResultVoid.Ok;
    }

    public Task<PublicIPAddressResource> CreateIp(string resourceGroup, string name, string region) {
        throw new NotImplementedException();
    }
}
