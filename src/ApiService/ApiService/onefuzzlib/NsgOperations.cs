using System.Threading.Tasks;
using Azure;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;

namespace Microsoft.OneFuzz.Service {
    public interface INsgOperations {
        Async.Task<NetworkSecurityGroupResource?> GetNsg(string name);
        public Async.Task<OneFuzzResult<bool>> AssociateSubnet(string name, VirtualNetworkResource vnet, SubnetResource subnet);
        IAsyncEnumerable<NetworkSecurityGroupResource> ListNsgs();
        bool OkToDelete(HashSet<string> active_regions, string nsg_region, string nsg_name);
        Async.Task<bool> StartDeleteNsg(string name);

        Async.Task<OneFuzzResultVoid> DissociateNic(Nsg nsg, NetworkInterfaceResource nic);

        Async.Task<OneFuzzResultVoid> Create(Nsg nsg);

        Async.Task<OneFuzzResultVoid> SetAllowedSources(Nsg nsg, NetworkSecurityGroupConfig nsgConfig);

        Async.Task<OneFuzzResultVoid> AssociateNic(Nsg nsg, NetworkInterfaceResource nic);

        Task<OneFuzzResultVoid> UpdateNsg(NetworkSecurityGroupData nsg);
    }


    public class NsgOperations : INsgOperations {
        private readonly ILogTracer _logTracer;

        private readonly IOnefuzzContext _context;


        public NsgOperations(ILogTracer logTracer, IOnefuzzContext context) {
            _logTracer = logTracer;
            _context = context;
        }

        public async Async.Task<OneFuzzResult<bool>> AssociateSubnet(string name, VirtualNetworkResource vnet, SubnetResource subnet) {
            var nsg = await GetNsg(name);
            if (nsg == null) {
                return OneFuzzResult<bool>.Error(new Error(ErrorCode.UNABLE_TO_FIND,
                    new[] { $"cannot associate subnet. nsg {name} not found" }));
            }

            if (nsg.Data.Location != vnet.Data.Location) {
                return OneFuzzResult<bool>.Error(new Error(ErrorCode.UNABLE_TO_UPDATE,
                    new[] {
                        $"subnet and nsg have to be in the same region. nsg {nsg.Data.Name} {nsg.Data.Location}, subnet: {subnet.Data.Name} {subnet.Data}"
                    }));
            }

            if (subnet.Data.NetworkSecurityGroup != null && subnet.Data.NetworkSecurityGroup.Id == nsg.Id) {
                _logTracer.Info($"Subnet {subnet.Data.Name} and NSG {name} already associated, not updating");
                return OneFuzzResult<bool>.Ok(true);
            }


            subnet.Data.NetworkSecurityGroup = nsg.Data;
            var result = await vnet.GetSubnets().CreateOrUpdateAsync(WaitUntil.Started, subnet.Data.Name, subnet.Data);
            return OneFuzzResult<bool>.Ok(true);
        }

        public async Async.Task<OneFuzzResultVoid> DissociateNic(Nsg nsg, NetworkInterfaceResource nic) {
            if (nic.Data.NetworkSecurityGroup == null) {
                return OneFuzzResultVoid.Ok;
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

            _logTracer.Info($"dissociating nic {nic.Data.Name} with nsg: {_context.Creds.GetBaseResourceGroup()} {nsg.Name}");
            nic.Data.NetworkSecurityGroup = null;
            try {
                await _context.Creds.GetResourceGroupResource()
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
                    return OneFuzzResultVoid.Ok;
                }
                return OneFuzzResultVoid.Error(
                    ErrorCode.UNABLE_TO_UPDATE,
                    new[] {
                        $"Unable to dissociate nsg {nsg.Name} with nic {nic.Data.Name} due to {e.Message} {e.StackTrace}"
                    }
                );
            }

            return OneFuzzResultVoid.Ok;
        }

        public async Async.Task<NetworkSecurityGroupResource?> GetNsg(string name) {
            try {
                var response = await _context.Creds.GetResourceGroupResource().GetNetworkSecurityGroupAsync(name);
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
            return _context.Creds.GetResourceGroupResource().GetNetworkSecurityGroups().GetAllAsync();
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
                var nsg = await _context.Creds.GetResourceGroupResource().GetNetworkSecurityGroupAsync(name);
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

        public async Task<OneFuzzResultVoid> Create(Nsg nsg) {
            if (await GetNsg(nsg.Name) != null) {
                return OneFuzzResultVoid.Ok;
            }

            return await CreateNsg(nsg.Name, nsg.Region);
        }

        private async Task<OneFuzzResultVoid> CreateNsg(string name, string location) {
            var resourceGroup = _context.Creds.GetBaseResourceGroup();
            _logTracer.Info($"creating nsg {resourceGroup}:{location}:{name}");

            var nsgParams = new NetworkSecurityGroupData {
                Location = location
            };

            var onefuzzOwner = _context.ServiceConfiguration.OneFuzzOwner;
            if (!string.IsNullOrEmpty(onefuzzOwner)) {
                if (!nsgParams.Tags.TryAdd("OWNER", onefuzzOwner)) {
                    _logTracer.Warning($"Failed to add tag 'OWNER':{onefuzzOwner} to nic {resourceGroup}:{name}");
                }
            }

            try {
                await _context.Creds.GetResourceGroupResource().GetNetworkSecurityGroups().CreateOrUpdateAsync(
                    WaitUntil.Started,
                    name,
                    nsgParams
                );
            } catch (RequestFailedException ex) {
                if (IsConcurrentRequestError(ex.Message)) {
                    // _logTracer.Debug($"create NSG had conflicts with concurrent request, ignoring {ex}");
                    return OneFuzzResultVoid.Ok;
                }
                return OneFuzzResultVoid.Error(
                    ErrorCode.UNABLE_TO_CREATE,
                    $"Unable to create nsg {name} due to {ex}"
                );
            }

            return OneFuzzResultVoid.Ok;
        }

        public async Task<OneFuzzResultVoid> SetAllowedSources(Nsg nsg, NetworkSecurityGroupConfig nsgConfig) {
            return await SetAllowed(nsg.Name, nsgConfig);
        }

        private async Task<OneFuzzResultVoid> SetAllowed(string name, NetworkSecurityGroupConfig sources) {
            var nsg = await GetNsg(name);
            if (nsg == null) {
                return OneFuzzResultVoid.Error(
                    ErrorCode.UNABLE_TO_FIND,
                    $"cannot update nsg rules. nsg {name} not found"
                );
            }

            _logTracer.Info(
                $"setting allowed incoming connection sources for nsg: {_context.Creds.GetBaseResourceGroup()} {name}"
            );

            var allSources = new List<string>();
            allSources.AddRange(sources.AllowedIps);
            allSources.AddRange(sources.AllowedServiceTags);

            // NSG security rule priority range defined here:
            // https://docs.microsoft.com/en-us/azure/virtual-network/network-security-groups-overview
            var minPriority = 100;
            // NSG rules per NSG limits:
            // https://docs.microsoft.com/en-us/azure/azure-resource-manager/management/azure-subscription-service-limits?toc=/azure/virtual-network/toc.json#networking-limits
            var maxRuleCount = 1000;

            if (allSources.Count > maxRuleCount) {
                return OneFuzzResultVoid.Error(
                    ErrorCode.INVALID_REQUEST,
                    $"too many rules provided {allSources.Count}. Max allowed: {maxRuleCount}"
                );
            }

            var priority = minPriority;
            nsg.Data.SecurityRules.Clear();
            foreach (var src in allSources) {
                // Will not exceed maxRuleCount or max NSG priority (4096)
                // due to earlier check of `allSources.Count`
                nsg.Data.SecurityRules.Add(new SecurityRuleData {
                    Name = $"Allow{priority}",
                    Protocol = new SecurityRuleProtocol("*"),
                    SourcePortRange = "*",
                    DestinationPortRange = "*",
                    SourceAddressPrefix = src,
                    DestinationAddressPrefix = "*",
                    Access = SecurityRuleAccess.Allow,
                    Priority = priority, // between 100 and 4096
                    Direction = SecurityRuleDirection.Inbound
                });
                priority++;
            }

            return await UpdateNsg(nsg.Data);
        }

        public async Task<OneFuzzResultVoid> AssociateNic(Nsg nsg, NetworkInterfaceResource nic) {
            return await AssociateNic(nsg.Name, nic);
        }

        private async Task<OneFuzzResultVoid> AssociateNic(string name, NetworkInterfaceResource nic) {
            var nsg = await GetNsg(name);
            if (nsg == null) {
                return OneFuzzResultVoid.Error(
                    ErrorCode.UNABLE_TO_FIND,
                    $"cannot associate nic. nsg {name} not found"
                );
            }

            if (nsg.Data.Location != nic.Data.Location) {
                return OneFuzzResultVoid.Error(
                    ErrorCode.UNABLE_TO_UPDATE,
                    new string[] {
                        "network interface and nsg have to be in the same region.",
                        $"nsg {nsg.Data.Name} {nsg.Data.Location}, nic: {nic.Data.Name} {nic.Data.Location}"
                    }
                );
            }

            if (nic.Data.NetworkSecurityGroup != null && nic.Data.NetworkSecurityGroup.Id == nsg.Id) {
                _logTracer.Info($"NIC {nic.Data.Name} and NSG {nsg.Data.Name} already associated, not updating");
                return OneFuzzResultVoid.Ok;
            }

            nic.Data.NetworkSecurityGroup = nsg.Data;
            _logTracer.Info($"associating nic {nic.Data.Name} with nsg: {_context.Creds.GetBaseResourceGroup()} {nsg.Data.Name}");

            try {
                await _context.Creds.GetResourceGroupResource().GetNetworkInterfaces().CreateOrUpdateAsync(
                    WaitUntil.Started, nic.Data.Name, nic.Data
                );
            } catch (RequestFailedException ex) {
                if (IsConcurrentRequestError(ex.Message)) {
                    // _logTracer.Debug($"associate NSG with NIC had conflicts with concurrent request, ignoring {ex}");
                    return OneFuzzResultVoid.Ok;
                }
                return OneFuzzResultVoid.Error(
                    ErrorCode.UNABLE_TO_UPDATE,
                    $"Unable to associate nsg {name} with nic {nic.Data.Name} due to {ex}"
                );
            }

            return OneFuzzResultVoid.Ok;
        }

        public async Task<OneFuzzResultVoid> UpdateNsg(NetworkSecurityGroupData nsg) {
            _logTracer.Info($"updating nsg {_context.Creds.GetBaseResourceGroup()}:{nsg.Location}:{nsg.Name}");

            try {
                await _context.Creds.GetResourceGroupResource().GetNetworkSecurityGroups().CreateOrUpdateAsync(
                    WaitUntil.Started,
                    nsg.Name,
                    nsg
                );
            } catch (RequestFailedException ex) {
                if (IsConcurrentRequestError(ex.Message)) {
                    //_logTracer.Debug($"create NSG had conflicts with concurrent request, ignoring {ex}");
                    return OneFuzzResultVoid.Ok;
                }

                return OneFuzzResultVoid.Error(
                    ErrorCode.UNABLE_TO_CREATE,
                    $"Unable to update nsg {nsg.Name} due to {ex}"
                );
            }

            return OneFuzzResultVoid.Ok;
        }
    }
}
