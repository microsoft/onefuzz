using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service.Functions;

public class TimerProxy {
    private readonly ILogger _logger;
    private readonly IOnefuzzContext _context;

    public TimerProxy(ILogger<TimerProxy> logTracer, IOnefuzzContext context) {
        _logger = logTracer;
        _context = context;
    }

    [Function("TimerProxy")]
    public async Async.Task Run([TimerTrigger("00:00:30")] TimerInfo myTimer) {

        var proxyOperations = _context.ProxyOperations;
        var scalesetOperations = _context.ScalesetOperations;
        var nsgOperations = _context.NsgOperations;

        var proxies = await proxyOperations.QueryAsync().ToListAsync();

        foreach (var p in proxies) {
            var proxy = p;

            if (VmStateHelper.Available.Contains(proxy.State)) {
                // Note, outdated checked at the start, but set at the end of this loop.
                // As this function is called via a timer, this works around a user
                // requesting to use the proxy while this function is checking if it's
                // out of date
                if (proxy.Outdated && !(await _context.ProxyOperations.IsUsed(proxy))) {
                    _logger.LogWarning("scaleset-proxy: outdated and not used: {Region}", proxy.Region);
                    proxy = await proxyOperations.SetState(proxy, VmState.Stopping);
                    // If something is "wrong" with a proxy, delete & recreate it
                } else if (!proxyOperations.IsAlive(proxy)) {
                    _logger.LogError("scaleset-proxy: alive check failed, stopping: {Region}", proxy.Region);
                    proxy = await proxyOperations.SetState(proxy, VmState.Stopping);
                } else {
                    await proxyOperations.SaveProxyConfig(proxy);
                }
            }

            if (VmStateHelper.NeedsWork.Contains(proxy.State)) {
                _logger.LogInformation("scaleset-proxy: update state. proxy:{Region} - {State}", proxy.Region, proxy.State);
                proxy = await proxyOperations.ProcessStateUpdate(proxy);
            }

            if (proxy is not null && proxy.State != VmState.Stopped && proxyOperations.IsOutdated(proxy)) {
                var r = await proxyOperations.Replace(proxy with { Outdated = true });
                if (!r.IsOk) {
                    _logger.AddHttpStatus(r.ErrorV);
                    _logger.LogError("Failed to replace proxy record for proxy {ProxyId}", proxy.ProxyId);
                }
            }
        }

        // make sure there is a proxy for every currently active region
        var regions = await scalesetOperations.QueryAsync().Select(x => x.Region).ToHashSetAsync();

        foreach (var region in regions) {
            var allOutdated = proxies.Where(x => x.Region == region).All(p => p.Outdated);
            if (allOutdated) {
                var proxy = await proxyOperations.GetOrCreate(region);
                _logger.LogInformation("Creating new proxy with id {ProxyId} in {Region}", proxy.ProxyId, region);
            }

            // this is required in order to support upgrade from non-nsg to
            // nsg enabled OneFuzz this will overwrite existing NSG
            // assignment though. This behavior is acceptable at this point
            // since we do not support bring your own NSG
            var nsgName = Nsg.NameFromRegion(region);

            if (await nsgOperations.GetNsg(nsgName) != null) {
                var network = await Network.Init(region, _context);

                var subnet = await network.GetSubnet();
                if (subnet != null) {
                    var vnet = await network.GetVnet();
                    if (vnet != null) {
                        var result = await nsgOperations.AssociateSubnet(nsgName, vnet, subnet);
                        if (!result.OkV) {
                            _logger.LogError("Failed to associate NSG and subnet due to {Error} in {Region}", result.ErrorV, region);
                        }
                    }
                }
            }
        }
        // if there are NSGs with name same as the region that they are allocated
        // and have no NIC associated with it then delete the NSG
        await foreach (var nsg in nsgOperations.ListNsgs()) {
            if (nsgOperations.OkToDelete(regions, nsg.Data.Location!, nsg.Data.Name)) {
                if (nsg.Data.NetworkInterfaces.Count == 0 && nsg.Data.Subnets.Count == 0) {
                    if (!await nsgOperations.StartDeleteNsg(nsg.Data.Name)) {
                        _logger.LogWarning("failed to start deleting NSG {NsgName}", nsg.Data.Name);
                    }
                }
            }
        }
    }
}
