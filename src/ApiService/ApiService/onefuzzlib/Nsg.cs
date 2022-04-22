using Azure;
using Azure.ResourceManager.Network;


namespace Microsoft.OneFuzz.Service
{
    public interface INsg
    {
        Async.Task<NetworkSecurityGroupResource?> GetNsg(string name);
        public Async.Task<Error?> AssociateSubnet(string name, VirtualNetworkResource vnet, SubnetResource subnet);
        IAsyncEnumerable<NetworkSecurityGroupResource> ListNsgs();
        bool OkToDelete(HashSet<string> active_regions, string nsg_region, string nsg_name);
        Async.Task<bool> StartDeleteNsg(string name);
    }


    public class Nsg : INsg
    {

        private readonly ICreds _creds;
        private readonly ILogTracer _logTracer;


        public Nsg(ICreds creds, ILogTracer logTracer)
        {
            _creds = creds;
            _logTracer = logTracer;
        }

        public async Async.Task<Error?> AssociateSubnet(string name, VirtualNetworkResource vnet, SubnetResource subnet)
        {
            var nsg = await GetNsg(name);
            if (nsg == null)
            {
                return new Error(ErrorCode.UNABLE_TO_FIND, new[] { $"cannot associate subnet. nsg {name} not found" });
            }

            if (nsg.Data.Location != vnet.Data.Location)
            {
                return new Error(ErrorCode.UNABLE_TO_UPDATE, new[] { $"subnet and nsg have to be in the same region. nsg {nsg.Data.Name} {nsg.Data.Location}, subnet: {subnet.Data.Name} {subnet.Data}" });
            }

            if (subnet.Data.NetworkSecurityGroup != null && subnet.Data.NetworkSecurityGroup.Id == nsg.Id)
            {
                _logTracer.Info($"Subnet {subnet.Data.Name} and NSG {name} already associated, not updating");
                return null;
            }


            subnet.Data.NetworkSecurityGroup = nsg.Data;
            var result = await vnet.GetSubnets().CreateOrUpdateAsync(WaitUntil.Started, subnet.Data.Name, subnet.Data);
            return null;
        }

        public async Async.Task<NetworkSecurityGroupResource?> GetNsg(string name)
        {
            var response = await _creds.GetResourceGroupResource().GetNetworkSecurityGroupAsync(name);
            if (response == null)
            {
                //_logTracer.Debug($"nsg %s does not exist: {name}");
            }
            return response?.Value;
        }

        public IAsyncEnumerable<NetworkSecurityGroupResource> ListNsgs()
        {
            return _creds.GetResourceGroupResource().GetNetworkSecurityGroups().GetAllAsync();
        }

        public bool OkToDelete(HashSet<string> active_regions, string nsg_region, string nsg_name)
        {
            return !active_regions.Contains(nsg_region) && nsg_region == nsg_name;
        }

        // Returns True if deletion completed (thus resource not found) or successfully started.
        // Returns False if failed to start deletion.
        public async Async.Task<bool> StartDeleteNsg(string name)
        {
            _logTracer.Info($"deleting nsg: {name}");
            var nsg = await _creds.GetResourceGroupResource().GetNetworkSecurityGroupAsync(name);
            await nsg.Value.DeleteAsync(WaitUntil.Completed);
            return true;
        }
    }
}