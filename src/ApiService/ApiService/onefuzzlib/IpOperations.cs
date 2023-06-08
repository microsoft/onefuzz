using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Faithlife.Utility;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;

public interface IIpOperations {
    public Async.Task<NetworkInterfaceResource?> GetPublicNic(string resourceGroup, string name);

    public Async.Task<OneFuzzResultVoid> CreatePublicNic(string resourceGroup, string name, Region region, Nsg? nsg);

    public Async.Task<string?> GetPublicIp(ResourceIdentifier resourceId);

    public Async.Task<string?> GetPublicIp(string resourceId);

    public Async.Task<PublicIPAddressResource?> GetIp(string resourceGroup, string name);

    public Async.Task DeleteNic(string resourceGroup, string name);

    public Async.Task DeleteIp(string resourceGroup, string name);

    public Async.Task<string?> GetScalesetInstanceIp(ScalesetId scalesetId, Guid machineId);

    public Async.Task CreateIp(string resourceGroup, string name, Region region);
}


public class IpOperations : IIpOperations {
    private readonly ILogger _logTracer;
    private readonly IOnefuzzContext _context;
    private readonly NetworkInterfaceQuery _networkInterfaceQuery;

    public IpOperations(ILogger<IpOperations> log, IOnefuzzContext context) {
        _logTracer = log;
        _context = context;
        _networkInterfaceQuery = new NetworkInterfaceQuery(context, log);
    }

    public async Async.Task<NetworkInterfaceResource?> GetPublicNic(string resourceGroup, string name) {
        _logTracer.LogInformation("getting nic: {ResourceGroup} - {Name}", resourceGroup, name);
        try {
            return await _context.Creds.GetResourceGroupResource().GetNetworkInterfaceAsync(name);
        } catch (RequestFailedException) {
            return null;
        }
    }

    public async Async.Task<PublicIPAddressResource?> GetIp(string resourceGroup, string name) {
        _logTracer.LogInformation("getting ip {ResourceGroup} - {Name}", resourceGroup, name);
        try {
            return await _context.Creds.GetResourceGroupResource().GetPublicIPAddressAsync(name);
        } catch (RequestFailedException) {
            return null;
        }
    }

    public async System.Threading.Tasks.Task DeleteNic(string resourceGroup, string name) {
        _logTracer.LogInformation("deleting nic {ResourceGroup} - {Name}", resourceGroup, name);
        var networkInterface = await _context.Creds.GetResourceGroupResource().GetNetworkInterfaceAsync(name);
        try {
            var r = await networkInterface.Value.DeleteAsync(WaitUntil.Started);
            if (r.GetRawResponse().IsError) {
                _logTracer.LogError("failed to start deleting nic {Name} due to {Error}", name, r.GetRawResponse().ReasonPhrase);
            }
        } catch (RequestFailedException ex) {
            _logTracer.LogError(ex, "DeleteNic");
            if (ex.ErrorCode != "NicReservedForAnotherVm") {
                throw;
            }
            _logTracer.LogWarning("unable to delete nic {ResourceGroup} - {Name} {Error}", resourceGroup, name, ex.Message);
        }
    }

    public async System.Threading.Tasks.Task DeleteIp(string resourceGroup, string name) {
        _logTracer.LogInformation("deleting ip {ResourceGroup} - {Name}", resourceGroup, name);
        var publicIpAddressAsync = await _context.Creds.GetResourceGroupResource().GetPublicIPAddressAsync(name);
        var r = await publicIpAddressAsync.Value.DeleteAsync(WaitUntil.Started);
        if (r.GetRawResponse().IsError) {
            _logTracer.LogError("failed to start deleting ip address {ResourceGroup} - {Name} due to {Error}", resourceGroup, name, r.GetRawResponse().ReasonPhrase);
        }
    }

    public async Task<string?> GetScalesetInstanceIp(ScalesetId scalesetId, Guid machineId) {
        var instance = await _context.VmssOperations.GetInstanceId(scalesetId, machineId);
        if (!instance.IsOk) {
            _logTracer.LogDebug("failed to get vmss {ScalesetId} for instance id {MachineId} due to {Error}", scalesetId, machineId, instance.ErrorV);
            return null;
        }

        var ips = await _networkInterfaceQuery.ListInstancePrivateIps(scalesetId, instance.OkV);
        return ips.FirstOrDefault();
    }
    public async Task<string?> GetPublicIp(string resourceId) {
        return await GetPublicIp(new ResourceIdentifier(resourceId));
    }

    public async Task<string?> GetPublicIp(ResourceIdentifier resourceId) {
        // TODO: Parts of this function seem redundant, but I'm mirroring
        // the python code exactly. We should revisit this.
        _logTracer.LogInformation("getting ip for {ResourceId}", resourceId);
        try {
            var resource = await (_context.Creds.GetData(_context.Creds.ParseResourceId(resourceId)));
            var networkInterfaces = await _context.Creds.GetResourceGroupResource().GetNetworkInterfaceAsync(
                resource.Data.Name
            );
            var publicIpConfigResource = (await networkInterfaces.Value.GetNetworkInterfaceIPConfigurations().FirstAsync());
            publicIpConfigResource = await publicIpConfigResource.GetAsync();
            var publicIp = publicIpConfigResource.Data.PublicIPAddress;
            if (publicIp == null) {
                return null;
            }

            resource = _context.Creds.ParseResourceId(publicIp.Id!);

            resource = await _context.Creds.GetData(resource);
            var publicIpResource = await _context.Creds.GetResourceGroupResource().GetPublicIPAddressAsync(
                resource.Data.Name
            );
            return publicIpResource.Value.Data.IPAddress;
        } catch (RequestFailedException ex) when (ex.Status == 404) {
            return null;
        }
    }

    public async Task<OneFuzzResultVoid> CreatePublicNic(string resourceGroup, string name, Region region, Nsg? nsg) {
        _logTracer.LogInformation("creating nic for {ResourceGroup} - {Name} in {Region}", resourceGroup, name, region);

        var network = await Network.Init(region, _context);
        var subnetId = await network.GetId();

        if (subnetId is null) {
            var r = await network.Create();
            if (!r.IsOk) {
                _logTracer.LogError("failed to create network in region {Region} due to {Error}", region, r.ErrorV);
            }
            return r;
        }

        if (nsg != null) {
            var subnet = await network.GetSubnet();
            if (subnet != null && subnet.Data.NetworkSecurityGroup == null) {
                var vnet = await network.GetVnet();
                var result = await _context.NsgOperations.AssociateSubnet(nsg.Name, vnet!, subnet);
                if (!result.IsOk) {
                    return OneFuzzResultVoid.Error(result.ErrorV);
                }
                return OneFuzzResultVoid.Ok;
            }
        }

        var ip = await GetIp(resourceGroup, name);
        if (ip == null) {
            await CreateIp(resourceGroup, name, region);
            return OneFuzzResultVoid.Ok;
        }

        var networkInterface = new NetworkInterfaceData {
            Location = region,
        };

        networkInterface.IPConfigurations.Add(new NetworkInterfaceIPConfigurationData {
            Name = "myIPConfig",
            PublicIPAddress = ip?.Data,
            Subnet = new SubnetData {
                Id = subnetId
            }
        }
        );

        var onefuzzOwner = _context.ServiceConfiguration.OneFuzzOwner;
        if (!string.IsNullOrEmpty(onefuzzOwner)) {
            if (!networkInterface.Tags.TryAdd("OWNER", onefuzzOwner)) {
                _logTracer.LogWarning("Failed to add tag 'OWNER':{Owner} to nic {ResourceGroup}:{Name}", onefuzzOwner, resourceGroup, name);
            }
        }

        try {
            var r =
                await _context.Creds.GetResourceGroupResource().GetNetworkInterfaces().CreateOrUpdateAsync(
                    WaitUntil.Started,
                    name,
                    networkInterface
                );

            if (r.GetRawResponse().IsError) {
                _logTracer.LogError("failed to createOrUpdate network interface {Name} due to {Error}", name, r.GetRawResponse().ReasonPhrase);
            }
        } catch (RequestFailedException ex) {
            if (!ex.ToString().Contains("RetryableError")) {
                return OneFuzzResultVoid.Error(
                    ErrorCode.VM_CREATE_FAILED,
                    $"unable to create nic: {ex}"
                );
            }
        }

        return OneFuzzResultVoid.Ok;
    }

    public async Async.Task CreateIp(string resourceGroup, string name, Region region) {
        var ipParams = new PublicIPAddressData() {
            Location = region,
            PublicIPAllocationMethod = NetworkIPAllocationMethod.Dynamic
        };

        var onefuzzOwner = _context.ServiceConfiguration.OneFuzzOwner;
        if (!string.IsNullOrEmpty(onefuzzOwner)) {
            if (!ipParams.Tags.TryAdd("OWNER", onefuzzOwner)) {
                _logTracer.LogWarning("Failed to add tag 'OWNER':{Owner} to ip {ResourceGroup}:{Name}", onefuzzOwner, resourceGroup, name);
            }
        }

        var r = await _context.Creds.GetResourceGroupResource().GetPublicIPAddresses().CreateOrUpdateAsync(
            WaitUntil.Started, name, ipParams
        );
        if (r.GetRawResponse().IsError) {
            _logTracer.LogError("Failed to create or update Public Ip Address {Name} due to {Error}", name, r.GetRawResponse().ReasonPhrase);
        }

        return;
    }


    /// <summary>
    /// Query the Scaleset network interface using the rest api directly because
    /// the api does not seems to support this :
    /// https://github.com/Azure/azure-sdk-for-net/issues/30253#issuecomment-1202447362
    /// </summary>
    sealed class NetworkInterfaceQuery {
        sealed record IpConfigurationsProperties(string privateIPAddress);

        sealed record IpConfigurations(IpConfigurationsProperties properties);

        sealed record NetworkInterfaceProperties(List<IpConfigurations> ipConfigurations);

        sealed record NetworkInterface(NetworkInterfaceProperties properties);

        sealed record ValueList<T>(List<T> value);

        private readonly IOnefuzzContext _context;

        private readonly ILogger _logTracer;

        public NetworkInterfaceQuery(IOnefuzzContext context, ILogger logTracer) {
            _context = context;
            _logTracer = logTracer;
        }


        public async Task<List<string>> ListInstancePrivateIps(ScalesetId scalesetId, string instanceId) {
            var token = _context.Creds.GetIdentity().GetToken(
                new TokenRequestContext(
                    new[] { $"https://management.azure.com" }));

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token.Token);
            var baseUrl = new Uri($"https://management.azure.com/");
            // https://docs.microsoft.com/en-us/rest/api/virtualnetwork/network-interface-in-vm-ss/get-virtual-machine-scale-set-network-interface?tabs=HTTP
            var requestURl = baseUrl + $"subscriptions/{_context.Creds.GetSubscription()}/resourceGroups/{_context.Creds.GetBaseResourceGroup()}/providers/Microsoft.Compute/virtualMachineScaleSets/{scalesetId}/virtualMachines/{instanceId}/networkInterfaces?api-version=2022-08-01";
            var response = await client.GetAsync(requestURl);
            if (response.IsSuccessStatusCode) {
                var responseStream = await response.Content.ReadAsStreamAsync();
                var nics = await JsonSerializer.DeserializeAsync<ValueList<NetworkInterface>>(responseStream);
                if (nics != null)
                    return nics.value.SelectMany(x => x.properties.ipConfigurations.Select(i => i.properties.privateIPAddress)).WhereNotNull().ToList();
            } else {
                var body = await response.Content.ReadAsStringAsync();
                _logTracer.LogError("failed to get ListInstancePrivateIps due to {body}", body);
            }
            return new List<string>();
        }
    }
}
