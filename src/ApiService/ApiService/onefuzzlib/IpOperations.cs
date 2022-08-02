using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.ResourceManager.Network;
using Faithlife.Utility;

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

    record IpConfigurationsProperties(string privateIPAddress);

    record IpConfigurations(IpConfigurationsProperties properties);

    record NetworkInterfaceProperties(List<IpConfigurations> ipConfigurations);

    record NetworkInterface(NetworkInterfaceProperties properties);

    record ValueList<T>(List<T> value);

    private async Task<List<string>> ListIps(Guid scalesetId, string instanceId) {

        var token = _context.Creds.GetIdentity().GetToken(
            new TokenRequestContext(
                new[] { $"https://management.azure.com" }));

        using HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token.Token);
        var baseUrl = new Uri($"https://management.azure.com/");
        var requestURl = baseUrl + $"subscriptions/{_context.Creds.GetSubscription()}/resourceGroups/{_context.Creds.GetBaseResourceGroup()}/providers/Microsoft.Compute/virtualMachineScaleSets/{scalesetId}/virtualMachines/{instanceId}/networkInterfaces?api-version=2021-08-01";
        var response = await client.GetAsync(requestURl);
        if (response.IsSuccessStatusCode) {
            var responseStream = await response.Content.ReadAsStreamAsync();
            var nics = await JsonSerializer.DeserializeAsync<ValueList<NetworkInterface>>(responseStream);
            if (nics != null)
                return nics.value.SelectMany(x => x.properties.ipConfigurations.Select(i => i.properties.privateIPAddress)).WhereNotNull().ToList();
        }

        return new List<string>();
    }

    public async Task<string?> GetScalesetInstanceIp(Guid scalesetId, Guid machineId) {
        var instance = await _context.VmssOperations.GetInstanceId(scalesetId, machineId);
        if (!instance.IsOk) {
            return null;
        }

        var ips = await ListIps(scalesetId, instance.OkV);
        return ips.FirstOrDefault();
    }
}
