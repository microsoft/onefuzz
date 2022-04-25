using Azure.ResourceManager.Network;

namespace Microsoft.OneFuzz.Service;

public interface IIpOperations
{
    public Async.Task<NetworkInterfaceResource> GetPublicNic(string resourceGroup, string name);

    public Async.Task<PublicIPAddressResource> GetIp(string resourceGroup, string name);

    public Async.Task DeleteNic(string resourceGroup, string name);

    public Async.Task DeleteIp(string resourceGroup, string name);
}

public class IpOperations : IIpOperations
{
    private ILogTracer _logTracer;

    private ICreds _creds;

    public IpOperations(ILogTracer log, ICreds creds)
    {
        _logTracer = log;
        _creds = creds;
    }

    public async Async.Task<NetworkInterfaceResource> GetPublicNic(string resourceGroup, string name)
    {
        _logTracer.Info($"getting nic: {resourceGroup} {name}");
        return await _creds.GetResourceGroupResource().GetNetworkInterfaceAsync(name);
    }

    public async Async.Task<PublicIPAddressResource> GetIp(string resourceGroup, string name)
    {
        _logTracer.Info($"getting ip {resourceGroup}:{name}");
        return await _creds.GetResourceGroupResource().GetPublicIPAddressAsync(name);
    }

    public System.Threading.Tasks.Task DeleteNic(string resourceGroup, string name)
    {
        throw new NotImplementedException();
    }

    public System.Threading.Tasks.Task DeleteIp(string resourceGroup, string name)
    {
        throw new NotImplementedException();
    }
}
