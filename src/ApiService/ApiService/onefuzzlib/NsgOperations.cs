using Azure;
using Azure.ResourceManager.Network;


namespace Microsoft.OneFuzz.Service {
    public interface INsgOperations {
        Async.Task<NetworkSecurityGroupResource?> GetNsg(string name);
        public Async.Task<Error?> AssociateSubnet(string name, VirtualNetworkResource vnet, SubnetResource subnet);
        IAsyncEnumerable<NetworkSecurityGroupResource> ListNsgs();
        bool OkToDelete(HashSet<string> active_regions, string nsg_region, string nsg_name);
        Async.Task<bool> StartDeleteNsg(string name);

        Async.Task<OneFuzzResultVoid> DissociateNic(Nsg nsg, NetworkInterfaceResource nic);
    }


    public class NsgOperations : INsgOperations {

        private readonly ICreds _creds;
        private readonly ILogTracer _logTracer;


        public NsgOperations(ICreds creds, ILogTracer logTracer) {
            _creds = creds;
            _logTracer = logTracer;
        }

        public async Async.Task<Error?> AssociateSubnet(string name, VirtualNetworkResource vnet, SubnetResource subnet) {
            var nsg = await GetNsg(name);
            if (nsg == null) {
                return new Error(ErrorCode.UNABLE_TO_FIND, new[] { $"cannot associate subnet. nsg {name} not found" });
            }

            if (nsg.Data.Location != vnet.Data.Location) {
                return new Error(ErrorCode.UNABLE_TO_UPDATE, new[] { $"subnet and nsg have to be in the same region. nsg {nsg.Data.Name} {nsg.Data.Location}, subnet: {subnet.Data.Name} {subnet.Data}" });
            }

            if (subnet.Data.NetworkSecurityGroup != null && subnet.Data.NetworkSecurityGroup.Id == nsg.Id) {
                _logTracer.Info($"Subnet {subnet.Data.Name} and NSG {name} already associated, not updating");
                return null;
            }


            subnet.Data.NetworkSecurityGroup = nsg.Data;
            var result = await vnet.GetSubnets().CreateOrUpdateAsync(WaitUntil.Started, subnet.Data.Name, subnet.Data);
            return null;
        }

        public async Async.Task<OneFuzzResultVoid> DissociateNic(Nsg nsg, NetworkInterfaceResource nic) {
            if (nic.Data.NetworkSecurityGroup == null) {
                return OneFuzzResultVoid.Ok();
            }

            var azureNsg = await GetNsg(nsg.Name);
            if (azureNsg == null) {
                return OneFuzzResultVoid.Error(
                    ErrorCode.UNABLE_TO_FIND,
                    new[] { $"cannot update nsg rules. nsg {nsg.Name} not found" }
                );
            }
            if (azureNsg.Data.Id != nic.Data.NetworkSecurityGroup.Id) {
                return OneFuzzResultVoid.Error(
                    ErrorCode.UNABLE_TO_UPDATE,
                    new[] {
                        "network interface is not associated with this nsg.",
                        $"nsg: {azureNsg.Id}, nic: {nic.Data.Name}, nic.nsg: {nic.Data.NetworkSecurityGroup.Id}"
                    }
                );
            }

            _logTracer.Info($"dissociating nic {nic.Data.Name} with nsg: {_creds.GetBaseResourceGroup()} {nsg.Name}");
            nic.Data.NetworkSecurityGroup = null;
            try {
                await _creds.GetResourceGroupResource()
                    .GetNetworkInterfaces()
                    .CreateOrUpdateAsync(WaitUntil.Started, nic.Data.Name, nic.Data);
            } catch (Exception e) {
                if (IsConcurrentRequestError(e.Message)) {
                    /*
                    logging.debug(
                        "dissociate nsg with nic had conflicts with ",
                        "concurrent request, ignoring %s",
                        err,
                    )
                    */
                    return OneFuzzResultVoid.Ok();
                }
                return OneFuzzResultVoid.Error(
                    ErrorCode.UNABLE_TO_UPDATE,
                    new[] {
                        $"Unable to dissociate nsg {nsg.Name} with nic {nic.Data.Name} due to {e.Message} {e.StackTrace}"
                    }
                );
            }

            return OneFuzzResultVoid.Ok();
        }

        public async Async.Task<NetworkSecurityGroupResource?> GetNsg(string name) {
            try {
                var response = await _creds.GetResourceGroupResource().GetNetworkSecurityGroupAsync(name);
                return response?.Value;
            } catch (RequestFailedException ex) {
                if (ex.ErrorCode == "ResourceNotFound") {
                    _logTracer.Verbose($"could not find nsg with name {name}");
                    return null;
                } else {
                    _logTracer.Exception(ex, $"failed to get nsg {name}");
                    throw;
                }
            }
        }

        public IAsyncEnumerable<NetworkSecurityGroupResource> ListNsgs() {
            return _creds.GetResourceGroupResource().GetNetworkSecurityGroups().GetAllAsync();
        }

        public bool OkToDelete(HashSet<string> active_regions, string nsg_region, string nsg_name) {
            return !active_regions.Contains(nsg_region) && nsg_region == nsg_name;
        }

        /// <summary>
        /// Returns True if deletion completed (thus resource not found) or successfully started.
        /// Returns False if failed to start deletion.
        /// </summary>
        public async Async.Task<bool> StartDeleteNsg(string name) {
            _logTracer.Info($"deleting nsg: {name}");
            try {
                var nsg = await _creds.GetResourceGroupResource().GetNetworkSecurityGroupAsync(name);
                await nsg.Value.DeleteAsync(WaitUntil.Completed);
                return true;
            } catch (RequestFailedException ex) {
                if (ex.ErrorCode == "ResourceNotFound") {
                    return true;
                } else {
                    _logTracer.Exception(ex, $"failed to delete nsg {name}");
                    throw;
                }
            }
        }

        private static bool IsConcurrentRequestError(string err) {
            return err.Contains("The request failed due to conflict with a concurrent request");
        }
    }
}
