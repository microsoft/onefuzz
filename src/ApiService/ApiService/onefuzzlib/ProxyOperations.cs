using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure.ResourceManager.Compute.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
namespace Microsoft.OneFuzz.Service;

public interface IProxyOperations : IStatefulOrm<Proxy, VmState> {
    Task<Proxy?> GetByProxyId(Guid proxyId);

    Async.Task<Proxy> SetState(Proxy proxy, VmState state);
    bool IsAlive(Proxy proxy);
    Async.Task SaveProxyConfig(Proxy proxy);
    bool IsOutdated(Proxy proxy);
    Async.Task<Proxy> GetOrCreate(Region region);
    Task<bool> IsUsed(Proxy proxy);

    // state transitions:
    Async.Task<Proxy> Init(Proxy proxy);
    Async.Task<Proxy> ExtensionsLaunch(Proxy proxy);
    Async.Task<Proxy> ExtensionsFailed(Proxy proxy);
    Async.Task<Proxy> VmAllocationFailed(Proxy proxy);
    Async.Task<Proxy> Running(Proxy proxy);
    Async.Task<Proxy> Stopping(Proxy proxy);
    Async.Task<Proxy> Stopped(Proxy proxy);
}
public class ProxyOperations : StatefulOrm<Proxy, VmState, ProxyOperations>, IProxyOperations {
    static readonly TimeSpan PROXY_LIFESPAN = TimeSpan.FromDays(7);

    public ProxyOperations(ILogger<ProxyOperations> log, IOnefuzzContext context)
        : base(log, context) {
        _logTracer.AddTag("Component", "scaleset-proxy");
    }



    public async Task<Proxy?> GetByProxyId(Guid proxyId) {
        var data = QueryAsync(filter: Query.RowKey(proxyId.ToString()));
        return await data.FirstOrDefaultAsync();
    }

    public async Async.Task<Proxy> GetOrCreate(Region region) {
        {
            var proxyList = QueryAsync(filter: Query.CreateQueryFilter($"PartitionKey eq {region.String} and outdated eq false"));
            await foreach (var proxy in proxyList) {
                if (IsOutdated(proxy)) {
                    var r1 = await Replace(proxy with { Outdated = true });
                    if (!r1.IsOk) {
                        _logTracer.AddHttpStatus(r1.ErrorV);
                        _logTracer.LogError("failed to replace record to mark proxy {ProxyId} as outdated", proxy.ProxyId);
                    }
                    continue;
                }

                if (!VmStateHelper.Available.Contains(proxy.State)) {
                    continue;
                }

                return proxy;
            }
        }

        _logTracer.LogInformation("creating proxy: region:{Region}", region);
        var newProxy = new Proxy(
            region,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            VmState.Init,
            new SecretValue<Authentication>(await AuthHelpers.BuildAuth(_logTracer)),
            null,
            null,
            _context.ServiceConfiguration.OneFuzzVersion,
            null,
            false);

        var r = await Replace(newProxy);
        if (!r.IsOk) {
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("failed to save new proxy {ProxyId} in {Region}", newProxy.ProxyId, region);
        }

        await _context.Events.SendEvent(new EventProxyCreated(region, newProxy.ProxyId));
        return newProxy;
    }

    public async Task<bool> IsUsed(Proxy proxy) {
        var forwards = await GetForwards(proxy);
        if (forwards.Count == 0) {
            _logTracer.LogInformation("no forwards {Region}", proxy.Region);
            return false;
        }
        return true;
    }

    public bool IsAlive(Proxy proxy) {
        var tenMinutesAgo = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10);

        if (proxy.Heartbeat is not null && proxy.Heartbeat.TimeStamp < tenMinutesAgo) {
            _logTracer.LogInformation("last heartbeat is more than an 10 minutes old: {Region} {LastHeartbeat} {ComparedToMinutesAgo}", proxy.Region, proxy.Heartbeat, tenMinutesAgo);
            return false;
        }

        if (proxy.Heartbeat is not null && proxy.Timestamp is not null && proxy.Timestamp < tenMinutesAgo) {
            _logTracer.LogError("no heartbeat in the last 10 minutes: {Region} {Timestamp} {ComparedToMinutesAgo}", proxy.Region, proxy.Timestamp, tenMinutesAgo);
            return false;
        }

        return true;
    }

    public bool IsOutdated(Proxy proxy) {
        if (!VmStateHelper.Available.Contains(proxy.State)) {
            return false;
        }

        if (proxy.Version != _context.ServiceConfiguration.OneFuzzVersion) {
            _logTracer.LogInformation("mismatch version: proxy:{ProxyVersion} {ServiceVersion} {State}", proxy.Version, _context.ServiceConfiguration.OneFuzzVersion, proxy.State);
            return true;
        }

        if (proxy.CreatedTimestamp is not null) {
            if (proxy.CreatedTimestamp < (DateTimeOffset.UtcNow - PROXY_LIFESPAN)) {
                _logTracer.LogInformation("proxy older than 7 days: {ProxyCreated} - {State}", proxy.CreatedTimestamp, proxy.State);
                return true;
            }
        }
        return false;
    }

    public async Async.Task SaveProxyConfig(Proxy proxy) {
        var forwards = await GetForwards(proxy);
        var url = (await _context.Containers.GetFileSasUrl(
            WellKnownContainers.ProxyConfigs,
            $"{proxy.Region}/{proxy.ProxyId}/config.json",
            StorageType.Config,
            BlobSasPermissions.Read)).EnsureNotNull("proxy configs container missing");

        var queueSas = await _context.Queue.GetQueueSas(
            "proxy",
            StorageType.Config,
            QueueSasPermissions.Add).EnsureNotNull("can't generate queue sas") ?? throw new Exception("Queue sas is null");

        var proxyConfig = new ProxyConfig(
            Url: url,
            Notification: queueSas,
            Region: proxy.Region,
            ProxyId: proxy.ProxyId,
            Forwards: forwards,
            InstanceTelemetryKey: _context.ServiceConfiguration.ApplicationInsightsInstrumentationKey.EnsureNotNull("missing InstrumentationKey"),
            MicrosoftTelemetryKey: _context.ServiceConfiguration.OneFuzzTelemetry,
            InstanceId: await _context.Containers.GetInstanceId());

        await _context.Containers.SaveBlob(WellKnownContainers.ProxyConfigs, $"{proxy.Region}/{proxy.ProxyId}/config.json", EntityConverter.ToJsonString(proxyConfig), StorageType.Config);
    }


    public async Async.Task<Proxy> SetState(Proxy proxy, VmState state) {
        if (proxy.State == state) {
            return proxy;
        }

        _logTracer.AddTags(new Dictionary<string, string> {
            { "ProxyId", proxy.ProxyId.ToString() },
            { "From", proxy.State.ToString()},
            { "To", state.ToString()}
        });

        _logTracer.LogEvent("SetState Proxy");

        var newProxy = proxy with { State = state };
        var r = await Replace(newProxy);
        if (!r.IsOk) {
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("Failed to replace proxy with {ProxyId}", newProxy.ProxyId);
        }
        await _context.Events.SendEvent(new EventProxyStateUpdated(newProxy.Region, newProxy.ProxyId, newProxy.State));
        return newProxy;
    }


    public async Async.Task<List<Forward>> GetForwards(Proxy proxy) {
        var forwards = new List<Forward>();

        await foreach (var entry in _context.ProxyForwardOperations.SearchForward(region: proxy.Region, proxyId: proxy.ProxyId)) {
            if (entry.EndTime < DateTimeOffset.UtcNow) {
                var r = await _context.ProxyForwardOperations.Delete(entry);
                if (!r.IsOk) {
                    _logTracer.AddHttpStatus(r.ErrorV);
                    _logTracer.LogError("failed to delete proxy forward for {ProxyId} in {Region}", proxy.ProxyId, proxy.Region);
                }
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
                var failedVmData = await _context.VmOperations.GetVmWithInstanceView(vm.Name);
                if (failedVmData is null) {
                    // this should exist since we just loaded the VM above
                    throw new InvalidOperationException("Unable to load instance-view data for VM");
                }

                return await SetProvisionFailed(proxy, failedVmData.InstanceView);
            } else {
                await SaveProxyConfig(proxy);
                return await SetState(proxy, VmState.ExtensionsLaunch);
            }
        } else {
            var nsg = Nsg.ForRegion(proxy.Region);
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
            var r = await Replace(proxy);
            if (!r.IsOk) {
                _logTracer.AddHttpStatus(r.ErrorV);
                _logTracer.LogError("Failed to save proxy {ProxyId}", proxy.ProxyId);
            }
            return proxy;
        }
    }

    private async System.Threading.Tasks.Task<Proxy> SetProvisionFailed(Proxy proxy, VirtualMachineInstanceView? instanceView) {
        var errors = GetErrors(proxy, instanceView);
        return await SetFailed(proxy, new Error(ErrorCode.PROXY_FAILED, errors.ToList()));
    }

    private async Task<Proxy> SetFailed(Proxy proxy, Error error) {
        if (proxy.Error is not null) {
            return proxy;
        }

        _logTracer.LogError("vm failed: {Region} -{Error}", proxy.Region, error);
        await _context.Events.SendEvent(new EventProxyFailed(proxy.Region, proxy.ProxyId, error));
        return await SetState(proxy with { Error = error }, VmState.Stopping);
    }


    private static IEnumerable<string> GetErrors(Proxy proxy, VirtualMachineInstanceView? instanceView) {
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
        return new Vm(
            // name should be less than 40 chars otherwise it gets truncated by azure
            Name: $"proxy-{proxy.ProxyId:N}",
            Region: proxy.Region,
            Sku: config.ProxyVmSku ?? "Standard_B2s",
            Image: config.DefaultLinuxVmImage ?? DefaultImages.Linux,
            Auth: proxy.Auth,
            Tags: tags,
            Nsg: null
        );
    }

    public async Task<Proxy> ExtensionsLaunch(Proxy proxy) {
        var config = await _context.ConfigOperations.Fetch();
        var vm = GetVm(proxy, config);
        var vmData = await _context.VmOperations.GetVm(vm.Name);
        if (vmData is null) {
            return await SetFailed(proxy, Error.Create(ErrorCode.PROXY_FAILED, "azure not able to find vm"));
        }

        if (vmData.ProvisioningState == "Failed") {
            var failedVmData = await _context.VmOperations.GetVmWithInstanceView(vm.Name);
            if (failedVmData is null) {
                // this should exist since we just loaded the VM above
                throw new InvalidOperationException("Unable to load instance-view data for VM");
            }

            return await SetProvisionFailed(proxy, failedVmData.InstanceView);
        }

        if (proxy.Ip is null) {
            // fetch and store IP
            var ip = await _context.IpOperations.GetPublicIp(vmData.NetworkProfile.NetworkInterfaces[0].Id);
            if (ip is null) {
                return proxy;
            }

            proxy = proxy with { Ip = ip };
            _ = await Update(proxy);
        }

        var extensions = await _context.Extensions.ProxyManagerExtensions(proxy.Region, proxy.ProxyId);
        var result = await _context.VmOperations.AddExtensions(vm,
            extensions
                .Select(e => e.GetAsVirtualMachineExtension())
                .ToDictionary(x => x.Item1, x => x.Item2));

        if (!result.IsOk) {
            return await SetFailed(proxy, result.ErrorV);
        }

        if (result.OkV) {
            // this means extensions are all ready - transition to Running state
            return await SetState(proxy, VmState.Running);
        }

        // not yet ready - do not transition state
        return proxy;
    }

    public async Task<Proxy> Stopping(Proxy proxy) {
        var config = await _context.ConfigOperations.Fetch();
        var vm = GetVm(proxy, config);
        if (!await _context.VmOperations.IsDeleted(vm)) {
            _logTracer.LogInformation("stopping proxy: {Region}", proxy.Region);
            if (await _context.VmOperations.Delete(vm)) {
                _logTracer.LogInformation("deleted proxy vm for region {Region}, name: {Name}", proxy.Region, vm.Name);
            }
            return proxy;
        }

        return await Stopped(proxy);
    }

    public async Task<Proxy> Stopped(Proxy proxy) {
        var stoppedVm = await SetState(proxy, VmState.Stopped);
        _logTracer.LogInformation("removing proxy: {Region}", stoppedVm.Region);
        await _context.Events.SendEvent(new EventProxyDeleted(stoppedVm.Region, stoppedVm.ProxyId));
        await Delete(stoppedVm).IgnoreResult();
        return stoppedVm;
    }

    public Task<Proxy> ExtensionsFailed(Proxy proxy) {
        // nothing to do
        return Async.Task.FromResult(proxy);
    }

    public Task<Proxy> VmAllocationFailed(Proxy proxy) {
        // nothing to do
        return Async.Task.FromResult(proxy);
    }

    public Task<Proxy> Running(Proxy proxy) {
        // nothing to do
        return Async.Task.FromResult(proxy);
    }
}
