using System.Threading.Tasks;
using Azure.ResourceManager.Network;

namespace Microsoft.OneFuzz.Service;


public interface ISubnet {
    Async.Task<VirtualNetworkResource?> GetVnet(string vnetName);

    Async.Task<SubnetResource?> GetSubnet(string vnetName, string subnetName);

    Async.Task<OneFuzzResultVoid> CreateVirtualNetwork(string resourceGroup, string name, string region, NetworkConfig networkConfig);

    Async.Task<string?> GetSubnetId(string name, string subnetName);
}

public class Subnet : ISubnet {
    private readonly ICreds _creds;

    private readonly ILogTracer _logTracer;

    public Subnet(ICreds creds, ILogTracer logTracer) {
        _creds = creds;
        _logTracer = logTracer;
    }

    public Task<OneFuzzResultVoid> CreateVirtualNetwork(string resourceGroup, string name, string region, NetworkConfig networkConfig) {
        _logTracer.Error($"network creation failed: {name}:{region} {{error}}");
        throw new NotImplementedException();
    }

    public async Async.Task<SubnetResource?> GetSubnet(string vnetName, string subnetName) {
        var vnet = await this.GetVnet(vnetName);

        if (vnet != null) {
            return await vnet.GetSubnetAsync(subnetName);
        }
        return null;
    }

    public async Task<string?> GetSubnetId(string name, string subnetName) {
        var subnet = await this.GetSubnet(name, subnetName);
        if (subnet != null) {
            return subnet.Id;
        }

        return null;
    }

    public async Async.Task<VirtualNetworkResource?> GetVnet(string vnetName) {
        var resourceGroupId = _creds.GetResourceGroupResourceIdentifier();
        var response = await _creds.ArmClient.GetResourceGroupResource(resourceGroupId).GetVirtualNetworkAsync(vnetName);
        return response.Value;
    }
}

