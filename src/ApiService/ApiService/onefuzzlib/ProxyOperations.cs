using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure.Storage.Sas;

namespace Microsoft.OneFuzz.Service;

public interface IProxyOperations : IStatefulOrm<Proxy, VmState> {
    Task<Proxy?> GetByProxyId(Guid proxyId);

    Async.Task SetState(Proxy proxy, VmState state);
    bool IsAlive(Proxy proxy);
    Async.Task SaveProxyConfig(Proxy proxy);
    bool IsOutdated(Proxy proxy);
    Async.Task<Proxy?> GetOrCreate(string region);
}
public class ProxyOperations : StatefulOrm<Proxy, VmState, ProxyOperations>, IProxyOperations {


    static TimeSpan PROXY_LIFESPAN = TimeSpan.FromDays(7);

    public ProxyOperations(ILogTracer log, IOnefuzzContext context)
        : base(log.WithTag("Component", "scaleset-proxy"), context) {

    }


    public async Task<Proxy?> GetByProxyId(Guid proxyId) {

        var data = QueryAsync(filter: $"RowKey eq '{proxyId}'");

        return await data.FirstOrDefaultAsync();
    }

    public async Async.Task<Proxy?> GetOrCreate(string region) {
        var proxyList = QueryAsync(filter: $"region eq '{region}' and outdated eq false");

        await foreach (var proxy in proxyList) {
            if (IsOutdated(proxy)) {
                await Replace(proxy with { Outdated = true });
                continue;
            }

            if (!VmStateHelper.Available.Contains(proxy.State)) {
                continue;
            }
            return proxy;
        }

        _logTracer.Info($"creating proxy: region:{region}");
        var newProxy = new Proxy(region, Guid.NewGuid(), DateTimeOffset.UtcNow, VmState.Init, Auth.BuildAuth(), null, null, _context.ServiceConfiguration.OneFuzzVersion, null, false);

        await Replace(newProxy);
        await _context.Events.SendEvent(new EventProxyCreated(region, newProxy.ProxyId));
        return newProxy;
    }

    public bool IsAlive(Proxy proxy) {
        var tenMinutesAgo = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10);

        if (proxy.Heartbeat != null && proxy.Heartbeat.TimeStamp < tenMinutesAgo) {
            _logTracer.Info($"last heartbeat is more than an 10 minutes old:  {proxy.Region} - last heartbeat:{proxy.Heartbeat} compared_to:{tenMinutesAgo}");
            return false;
        }

        if (proxy.Heartbeat != null && proxy.TimeStamp != null && proxy.TimeStamp < tenMinutesAgo) {
            _logTracer.Error($"no heartbeat in the last 10 minutes: {proxy.Region} timestamp: {proxy.TimeStamp} compared_to:{tenMinutesAgo}");
            return false;
        }

        return true;
    }

    public bool IsOutdated(Proxy proxy) {
        if (!VmStateHelper.Available.Contains(proxy.State)) {
            return false;
        }

        if (proxy.Version != _context.ServiceConfiguration.OneFuzzVersion) {
            _logTracer.Info($"mismatch version: proxy:{proxy.Version} service:{_context.ServiceConfiguration.OneFuzzVersion} state:{proxy.State}");
            return true;
        }

        if (proxy.CreatedTimestamp != null) {
            if (proxy.CreatedTimestamp < (DateTimeOffset.UtcNow - PROXY_LIFESPAN)) {
                _logTracer.Info($"proxy older than 7 days:proxy-created:{proxy.CreatedTimestamp} state:{proxy.State}");
                return true;
            }
        }
        return false;
    }

    public async Async.Task SaveProxyConfig(Proxy proxy) {
        var forwards = await GetForwards(proxy);
        var url = (await _context.Containers.GetFileSasUrl(new Container("proxy-configs"), $"{proxy.Region}/{proxy.ProxyId}/config.json", StorageType.Config, BlobSasPermissions.Read)).EnsureNotNull("Can't generate file sas");
        var queueSas = await _context.Queue.GetQueueSas("proxy", StorageType.Config, QueueSasPermissions.Add).EnsureNotNull("can't generate queue sas") ?? throw new Exception("Queue sas is null");

        var proxyConfig = new ProxyConfig(
            Url: url,
            Notification: queueSas,
            Region: proxy.Region,
            ProxyId: proxy.ProxyId,
            Forwards: forwards,
            InstanceTelemetryKey: _context.ServiceConfiguration.ApplicationInsightsInstrumentationKey.EnsureNotNull("missing InstrumentationKey"),
            MicrosoftTelemetryKey: _context.ServiceConfiguration.OneFuzzTelemetry.EnsureNotNull("missing Telemetry"),
            InstanceId: await _context.Containers.GetInstanceId());

        await _context.Containers.SaveBlob(new Container("proxy-configs"), $"{proxy.Region}/{proxy.ProxyId}/config.json", _entityConverter.ToJsonString(proxyConfig), StorageType.Config);
    }


    public async Async.Task SetState(Proxy proxy, VmState state) {
        if (proxy.State == state) {
            return;
        }

        await Replace(proxy with { State = state });

        await _context.Events.SendEvent(new EventProxyStateUpdated(proxy.Region, proxy.ProxyId, proxy.State));
    }


    public async Async.Task<List<Forward>> GetForwards(Proxy proxy) {
        var forwards = new List<Forward>();

        await foreach (var entry in _context.ProxyForwardOperations.SearchForward(region: proxy.Region, proxyId: proxy.ProxyId)) {
            if (entry.EndTime < DateTimeOffset.UtcNow) {
                await _context.ProxyForwardOperations.Delete(entry);
            } else {
                forwards.Add(new Forward(long.Parse(entry.Port), entry.DstPort, entry.DstIp));
            }
        }
        return forwards;
    }
}
