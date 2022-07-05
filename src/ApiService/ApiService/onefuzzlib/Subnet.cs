using Azure.ResourceManager.Network;

namespace Microsoft.OneFuzz.Service;


public interface ISubnet {
    Async.Task<VirtualNetworkResource?> GetVnet(string vnetName);

    Async.Task<SubnetResource?> GetSubnet(string vnetName, string subnetName);
}

public class Subnet : ISubnet {
    private readonly ICreds _creds;

    public Subnet(ICreds creds) {
        _creds = creds;
    }

    public async Async.Task<SubnetResource?> GetSubnet(string vnetName, string subnetName) {
        var vnet = await this.GetVnet(vnetName);

        if (vnet != null) {
            return await vnet.GetSubnetAsync(subnetName);
        }
        return null;
    }

    public async Async.Task<VirtualNetworkResource?> GetVnet(string vnetName) {
        var resourceGroupId = _creds.GetResourceGroupResourceIdentifier();
        var response = await _creds.ArmClient.GetResourceGroupResource(resourceGroupId).GetVirtualNetworkAsync(vnetName);
        return response.Value;
    }
}

