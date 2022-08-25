using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.Storage.Sas;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IProxyOperations : IStatefulOrm<Proxy, VmState> {
    Task<Proxy?> GetByProxyId(Guid proxyId);

    Async.Task<Proxy> SetState(Proxy proxy, VmState state);
    bool IsAlive(Proxy proxy);
    Async.Task SaveProxyConfig(Proxy proxy);
    bool IsOutdated(Proxy proxy);
    Async.Task<Proxy?> GetOrCreate(string region);

    Task<bool> IsUsed(Proxy proxy);
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

    public async Task<bool> IsUsed(Proxy proxy) {
        var forwards = await GetForwards(proxy);
        if (forwards.Count == 0) {
            _logTracer.Info($"no forwards {proxy.Region}");
            return false;
        }
        return true;
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

        await _context.Containers.SaveBlob(new Container("proxy-configs"), $"{proxy.Region}/{proxy.ProxyId}/config.json", EntityConverter.ToJsonString(proxyConfig), StorageType.Config);
    }


    public async Async.Task<Proxy> SetState(Proxy proxy, VmState state) {
        if (proxy.State == state) {
            return proxy;
        }

        var newProxy = proxy with { State = state };
        await Replace(newProxy);
        await _context.Events.SendEvent(new EventProxyStateUpdated(proxy.Region, proxy.ProxyId, proxy.State));
        return newProxy;
    }


    public async Async.Task<List<Forward>> GetForwards(Proxy proxy) {
        var forwards = new List<Forward>();

        await foreach (var entry in _context.ProxyForwardOperations.SearchForward(region: proxy.Region, proxyId: proxy.ProxyId)) {
            if (entry.EndTime < DateTimeOffset.UtcNow) {
                await _context.ProxyForwardOperations.Delete(entry);
            } else {
                forwards.Add(new Forward(entry.Port, entry.DstPort, entry.DstIp));
            }
        }
        return forwards;
    }

    public async Async.Task<Proxy> Init(Proxy proxy) {
        var config = await _context.ConfigOperations.Fetch();
        var vm = GetVm(proxy, config);
        var vmData = await _context.VmOperations.GetVm(vm.Name);

        if (vmData != null) {
            if (vmData.ProvisioningState == "Failed") {
                return await SetProvisionFailed(proxy, vmData);
            } else {
                await SaveProxyConfig(proxy);
                return await SetState(proxy, VmState.ExtensionsLaunch);
            }
        } else {
            var nsg = new Nsg(proxy.Region, proxy.Region);
            var result = await _context.NsgOperations.Create(nsg);
            if (!result.IsOk) {
                return await SetFailed(proxy, result.ErrorV);
            }

            var nsgConfig = config.ProxyNsgConfig;
            var result2 = await _context.NsgOperations.SetAllowedSources(nsg, nsgConfig);

            if (!result2.IsOk) {
                return await SetFailed(proxy, result2.ErrorV);
            }

            var result3 = await _context.VmOperations.Create(vm with { Nsg = nsg });

            if (!result3.IsOk) {
                return await SetFailed(proxy, result3.ErrorV);
            }
            return proxy;
        }
    }

    private async System.Threading.Tasks.Task<Proxy> SetProvisionFailed(Proxy proxy, VirtualMachineData vmData) {
        var errors = GetErrors(proxy, vmData).ToArray();
        await SetFailed(proxy, new Error(ErrorCode.PROXY_FAILED, errors));
        return proxy;
    }

    private async Task<Proxy> SetFailed(Proxy proxy, Error error) {
        if (proxy.Error != null) {
            return proxy;
        }

        _logTracer.Error($"vm failed: {proxy.Region} -{error}");
        await _context.Events.SendEvent(new EventProxyFailed(proxy.Region, proxy.ProxyId, error));
        return await SetState(proxy with { Error = error }, VmState.Stopping);
    }


    private static IEnumerable<string> GetErrors(Proxy proxy, VirtualMachineData vmData) {
        var instanceView = vmData.InstanceView;
        yield return "provisioning failed";
        if (instanceView is null) {
            yield break;
        }

        foreach (var status in instanceView.Statuses) {
            if (status.Level == StatusLevelTypes.Error) {
                yield return $"code:{status.Code} status:{status.DisplayStatus} message:{status.Message}";
            }
        }
    }

    public static Vm GetVm(Proxy proxy, InstanceConfig config) {
        var tags = config.VmssTags;
        const string PROXY_IMAGE = "Canonical:UbuntuServer:18.04-LTS:latest";
        return new Vm(
            // name should be less than 40 chars otherwise it gets truncated by azure
            Name: $"proxy-{proxy.ProxyId:N}",
            Region: proxy.Region,
            Sku: config.ProxyVmSku,
            Image: PROXY_IMAGE,
            Auth: proxy.Auth,
            Tags: config.VmssTags,
            Nsg: null
        );
    }

    public async Task<Proxy> ExtensionsLaunch(Proxy proxy) {
        var config = await _context.ConfigOperations.Fetch();
        var vm = GetVm(proxy, config);
        var vmData = await _context.VmOperations.GetVm(vm.Name);

        if (vmData == null) {
            return await SetFailed(proxy, new Error(ErrorCode.PROXY_FAILED, new[] { "azure not able to find vm" }));
        }

        if (vmData.ProvisioningState == "Failed") {
            return await SetProvisionFailed(proxy, vmData);
        }

        var ip = await _context.IpOperations.GetPublicIp(vmData.NetworkProfile.NetworkInterfaces[0].Id);
        if (ip == null) {
            return proxy;
        }

        var newProxy = proxy with { Ip = ip };

        var extensions = await _context.Extensions.ProxyManagerExtensions(newProxy.Region, newProxy.ProxyId);
        var result = await _context.VmOperations.AddExtensions(vm,
            extensions
                .Select(e => e.GetAsVirtualMachineExtension())
                .ToDictionary(x => x.Item1, x => x.Item2));

        if (!result.IsOk) {
            return await SetFailed(newProxy, result.ErrorV);
        }

        return await SetState(newProxy, VmState.Running);
    }

    public async Task<Proxy> Stopping(Proxy proxy) {
        var config = await _context.ConfigOperations.Fetch();
        var vm = GetVm(proxy, config);
        if (!await _context.VmOperations.IsDeleted(vm)) {
            _logTracer.Error($"stopping proxy: {proxy.Region}");
            await _context.VmOperations.Delete(vm);
            return proxy;
        }

        return await Stopped(proxy);
    }

    private async Task<Proxy> Stopped(Proxy proxy) {
        var stoppedVm = await SetState(proxy, VmState.Stopped);
        _logTracer.Info($"removing proxy: {proxy.Region}");
        await _context.Events.SendEvent(new EventProxyDeleted(proxy.Region, proxy.ProxyId));
        await Delete(proxy);
        return stoppedVm;
    }
}
