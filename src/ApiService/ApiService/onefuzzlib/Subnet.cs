using Azure.ResourceManager.Network;
namespace Microsoft.OneFuzz.Service;

public interface ISubnet
{
    System.Threading.Tasks.Task<VirtualNetworkResource?> GetVnet(string vnetName);

    System.Threading.Tasks.Task<SubnetResource?> GetSubnet(string vnetName, string subnetName);
}

public partial class TimerProxy
{
    public class Subnet : ISubnet
    {
        private readonly ICreds _creds;

        public Subnet(ICreds creds)
        {
            _creds = creds;
        }

        public async System.Threading.Tasks.Task<SubnetResource?> GetSubnet(string vnetName, string subnetName)
        {
            var vnet = await this.GetVnet(vnetName);

            if (vnet != null)
            {
                return await vnet.GetSubnetAsync(subnetName);
            }
            return null;
        }

        public async System.Threading.Tasks.Task<VirtualNetworkResource?> GetVnet(string vnetName)
        {
            var resourceGroupId = _creds.GetResourceGroupResourceIdentifier();
            var response = await _creds.ArmClient.GetResourceGroupResource(resourceGroupId).GetVirtualNetworkAsync(vnetName);
            return response.Value;
        }
    }
}

