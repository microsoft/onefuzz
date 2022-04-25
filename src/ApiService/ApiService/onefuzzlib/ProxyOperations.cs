using ApiService.OneFuzzLib.Orm;
using Azure.Storage.Sas;
using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service;

public interface IProxyOperations : IStatefulOrm<Proxy, VmState>
{
    Task<Proxy?> GetByProxyId(Guid proxyId);

    Async.Task SetState(Proxy proxy, VmState state);
    bool IsAlive(Proxy proxy);
    System.Threading.Tasks.Task SaveProxyConfig(Proxy proxy);
    bool IsOutdated(Proxy proxy);
    System.Threading.Tasks.Task<Proxy?> GetOrCreate(string region);
}
public class ProxyOperations : StatefulOrm<Proxy, VmState>, IProxyOperations
{

    private readonly IEvents _events;
    private readonly IProxyForwardOperations _proxyForwardOperations;
    private readonly IContainers _containers;
    private readonly IQueue _queue;
    private readonly ICreds _creds;

    static TimeSpan PROXY_LIFESPAN = TimeSpan.FromDays(7);

    public ProxyOperations(ILogTracer log, IStorage storage, IEvents events, IProxyForwardOperations proxyForwardOperations, IContainers containers, IQueue queue, ICreds creds, IServiceConfig config)
            : base(storage, log.WithTag("Component", "scaleset-proxy"), config)
    {
        _events = events;
        _proxyForwardOperations = proxyForwardOperations;
        _containers = containers;
        _queue = queue;
        _creds = creds;
    }

    public async Task<Proxy?> GetByProxyId(Guid proxyId)
    {

        var data = QueryAsync(filter: $"RowKey eq '{proxyId}'");

        return await data.FirstOrDefaultAsync();
    }

    public async System.Threading.Tasks.Task<Proxy?> GetOrCreate(string region)
    {
        var proxyList = QueryAsync(filter: $"region eq '{region}' and outdated eq false");

        await foreach (var proxy in proxyList)
        {
            if (IsOutdated(proxy))
            {
                await Replace(proxy with { Outdated = true });
                continue;
            }

            if (!VmStateHelper.Available().Contains(proxy.State))
            {
                continue;
            }
            return proxy;
        }

        _logTracer.Info($"creating proxy: region:{region}");
        var newProxy = new Proxy(region, Guid.NewGuid(), DateTimeOffset.UtcNow, VmState.Init, Auth.BuildAuth(), null, null, _config.OnefuzzVersion, null, false);

        await Replace(newProxy);
        await _events.SendEvent(new EventProxyCreated(region, newProxy.ProxyId));
        return newProxy;
    }

    public bool IsAlive(Proxy proxy)
    {
        var tenMinutesAgo = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10);

        if (proxy.Heartbeat != null && proxy.Heartbeat.TimeStamp < tenMinutesAgo)
        {
            _logTracer.Info($"last heartbeat is more than an 10 minutes old:  {proxy.Region} - last heartbeat:{proxy.Heartbeat} compared_to:{tenMinutesAgo}");
            return false;
        }

        if (proxy.Heartbeat != null && proxy.TimeStamp != null && proxy.TimeStamp < tenMinutesAgo)
        {
            _logTracer.Error($"no heartbeat in the last 10 minutes: {proxy.Region} timestamp: {proxy.TimeStamp} compared_to:{tenMinutesAgo}");
            return false;
        }

        return true;
    }

    public bool IsOutdated(Proxy proxy)
    {
        if (!VmStateHelper.Available().Contains(proxy.State))
        {
            return false;
        }

        if (proxy.Version != _config.OnefuzzVersion)
        {
            _logTracer.Info($"mismatch version: proxy:{proxy.Version} service:{_config.OnefuzzVersion} state:{proxy.State}");
            return true;
        }

        if (proxy.CreatedTimestamp != null)
        {
            if (proxy.CreatedTimestamp < (DateTimeOffset.UtcNow - PROXY_LIFESPAN))
            {
                _logTracer.Info($"proxy older than 7 days:proxy-created:{proxy.CreatedTimestamp} state:{proxy.State}");
                return true;
            }
        }
        return false;
    }

    public async System.Threading.Tasks.Task SaveProxyConfig(Proxy proxy)
    {
        var forwards = await GetForwards(proxy);
        var url = (await _containers.GetFileSasUrl(new Container("proxy-configs"), $"{proxy.Region}/{proxy.ProxyId}/config.json", StorageType.Config, BlobSasPermissions.Read)).EnsureNotNull("Can't generate file sas");

        var proxyConfig = new ProxyConfig(
            Url: url,
            Notification: _queue.GetQueueSas("proxy", StorageType.Config, QueueSasPermissions.Add).EnsureNotNull("can't generate queue sas"),
            Region: proxy.Region,
            ProxyId: proxy.ProxyId,
            Forwards: forwards,
            InstanceTelemetryKey: _config.ApplicationInsightsInstrumentationKey.EnsureNotNull("missing InstrumentationKey"),
            MicrosoftTelemetryKey: _config.OneFuzzTelemetry.EnsureNotNull("missing Telemetry"),
            InstanceId: await _containers.GetInstanceId());


        await _containers.saveBlob(new Container("proxy-configs"), $"{proxy.Region}/{proxy.ProxyId}/config.json", _entityConverter.ToJsonString(proxyConfig), StorageType.Config);
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


    public async Async.Task<List<Forward>> GetForwards(Proxy proxy)
    {
        var forwards = new List<Forward>();

        await foreach (var entry in _proxyForwardOperations.SearchForward(region: proxy.Region, proxyId: proxy.ProxyId))
        {
            if (entry.EndTime < DateTimeOffset.UtcNow)
            {
                await _proxyForwardOperations.Delete(entry);
            }
            else
            {
                forwards.Add(new Forward(entry.Port, entry.DstPort, entry.DstIp));
            }
        }
        return forwards;
    }
}
