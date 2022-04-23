using ApiService.OneFuzzLib.Orm;
using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service;

public interface IProxyOperations : IStatefulOrm<Proxy, VmState>
{
    Task<Proxy?> GetByProxyId(Guid proxyId);

    Async.Task SetState(Proxy proxy, VmState state);
    bool IsAlive(Proxy proxy);
    System.Threading.Tasks.Task SaveProxyConfig(Proxy proxy);
    bool IsOutdated(Proxy proxy);
    System.Threading.Tasks.Task GetOrCreate(string region);
}
public class ProxyOperations : StatefulOrm<Proxy, VmState>, IProxyOperations
{
    private readonly ILogTracer _log;

    private readonly IEvents _events;

    public ProxyOperations(ILogTracer log, IStorage storage, IEvents events, IServiceConfig config)
        : base(storage, log, config)
    {
        _log = log;
        _events = events;
    }

    public async Task<Proxy?> GetByProxyId(Guid proxyId)
    {

        var data = QueryAsync(filter: $"RowKey eq '{proxyId}'");

        return await data.FirstOrDefaultAsync();
    }

    public System.Threading.Tasks.Task GetOrCreate(string region)
    {
        throw new NotImplementedException();
    }

    public bool IsAlive(Proxy proxy)
    {
        throw new NotImplementedException();
    }

    public bool IsOutdated(Proxy proxy)
    {
        throw new NotImplementedException();
    }

    public System.Threading.Tasks.Task SaveProxyConfig(Proxy proxy)
    {
        throw new NotImplementedException();
    }

    public async Async.Task SetState(Proxy proxy, VmState state)
    {
        if (proxy.State == state)
        {
            return;
        }

        await Replace(proxy with { State = state });

        await _events.SendEvent(new EventProxyStateUpdated(proxy.Region, proxy.ProxyId, proxy.State));
    }
}
