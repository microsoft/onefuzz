using Azure.Core;
using Azure.ResourceManager.Network;

namespace Microsoft.OneFuzz.Service;

public class Network {
    private readonly string _name;
    private readonly string _group;
    private readonly string _region;
    private readonly IOnefuzzContext _context;

    private readonly NetworkConfig _networkConfig;

    // This was generated randomly and should be preserved moving forwards
    static Guid NETWORK_GUID_NAMESPACE = Guid.Parse("372977ad-b533-416a-b1b4-f770898e0b11");

    public Network(string region, string group, string name, IOnefuzzContext context, NetworkConfig networkConfig) {
        _region = region;
        _group = group;
        _name = name;
        _context = context;
        _networkConfig = networkConfig;
    }

    public static async Async.Task<Network> Create(string region, IOnefuzzContext context) {
        var group = context.Creds.GetBaseResourceGroup();
        var instanceConfig = await context.ConfigOperations.Fetch();
        var networkConfig = instanceConfig.NetworkConfig;

        // Network names will be calculated from the address_space/subnet
        // *except* if they are the original values.  This allows backwards
        // compatibility to existing configs if you don't change the network
        // configs.

        string name;

        if (networkConfig.AddressSpace == NetworkConfig.Default.AddressSpace && networkConfig.Subnet == NetworkConfig.Default.Subnet) {
            name = region;
        } else {
            var networkId = Faithlife.Utility.GuidUtility.Create(NETWORK_GUID_NAMESPACE, string.Join("|", networkConfig.AddressSpace, networkConfig.Subnet), 5);
            name = $"{region}-{networkId}";
        }


        return new Network(region, group, name, context, networkConfig);
    }

    public Async.Task<SubnetResource?> GetSubnet() {
        return _context.Subnet.GetSubnet(_name, _name);
    }

    public async Async.Task<ResourceIdentifier?> GetId() {
        return await _context.Subnet.GetSubnetId(this._name, this._name);
    }

    public async Async.Task<OneFuzzResultVoid> Create() {
        if (!await Exists()) {
            return await _context.Subnet.CreateVirtualNetwork(_group, _name, _region, _networkConfig);
        }

        return OneFuzzResultVoid.Ok;
    }

    public async Async.Task<bool> Exists() {
        var id = await GetId();
        return id is not null;
    }

    internal Async.Task<VirtualNetworkResource?> GetVnet() {
        return _context.Subnet.GetVnet(_name);
    }
}
