﻿using Microsoft.Azure.Functions.Worker;

namespace Microsoft.OneFuzz.Service.Functions;

public class TimerProxy {
    private readonly ILogTracer _logger;
    private readonly IOnefuzzContext _context;

    public TimerProxy(ILogTracer logTracer, IOnefuzzContext context) {
        _logger = logTracer;
        _context = context;
    }

    [Function("TimerProxy")]
    public async Async.Task Run([TimerTrigger("00:00:30")] TimerInfo myTimer) {

        var proxyOperations = _context.ProxyOperations;
        var scalesetOperations = _context.ScalesetOperations;
        var nsgOpertions = _context.NsgOperations;

        var proxies = await proxyOperations.QueryAsync().ToListAsync();

        foreach (var p in proxies) {
            var proxy = p;

            if (VmStateHelper.Available.Contains(proxy.State)) {
                // Note, outdated checked at the start, but set at the end of this loop.
                // As this function is called via a timer, this works around a user
                // requesting to use the proxy while this function is checking if it's
                // out of date
                if (proxy.Outdated && !(await _context.ProxyOperations.IsUsed(proxy))) {
                    _logger.Warning($"scaleset-proxy: outdated and not used: {proxy.Region}");
                    await proxyOperations.SetState(proxy, VmState.Stopping);
                    // If something is "wrong" with a proxy, delete & recreate it
                } else if (!proxyOperations.IsAlive(proxy)) {
                    _logger.Error($"scaleset-proxy: alive check failed, stopping: {proxy.Region}");
                    await proxyOperations.SetState(proxy, VmState.Stopping);
                } else {
                    await proxyOperations.SaveProxyConfig(proxy);
                }
            }

            if (VmStateHelper.NeedsWork.Contains(proxy.State)) {
                _logger.Info($"scaleset-proxy: update state. proxy:{proxy.Region} state:{proxy.State}");
                proxy = await proxyOperations.ProcessStateUpdate(proxy);
            }

            if (proxy is not null && (proxy.State != VmState.Stopped && proxyOperations.IsOutdated(proxy))) {
                var r = await proxyOperations.Replace(proxy with { Outdated = true });
                if (!r.IsOk) {
                    _logger.Error($"Failed to replace proxy recordy for proxy {proxy.ProxyId} due to {r.ErrorV}");
                }
            }
        }

        // make sure there is a proxy for every currently active region
        var regions = await scalesetOperations.QueryAsync().Select(x => x.Region).ToHashSetAsync();

        foreach (var region in regions) {
            var allOutdated = proxies.Where(x => x.Region == region).All(p => p.Outdated);
            if (allOutdated) {
                var proxy = await proxyOperations.GetOrCreate(region);
                _logger.Info($"Creating new proxy with id {proxy.ProxyId} in region {region}");
            }

            // this is required in order to support upgrade from non-nsg to
            // nsg enabled OneFuzz this will overwrite existing NSG
            // assignment though. This behavior is acceptable at this point
            // since we do not support bring your own NSG
            var nsgName = Nsg.NameFromRegion(region);

            if (await nsgOpertions.GetNsg(nsgName) != null) {
                var network = await Network.Init(region, _context);

                var subnet = await network.GetSubnet();
                if (subnet != null) {
                    var vnet = await network.GetVnet();
                    if (vnet != null) {
                        var result = await nsgOpertions.AssociateSubnet(nsgName, vnet, subnet);
                        if (!result.OkV) {
                            _logger.Error($"Failed to associate NSG and subnet due to {result.ErrorV} in region {region}");
                        }
                    }
                }
            }
        }
        // if there are NSGs with name same as the region that they are allocated
        // and have no NIC associated with it then delete the NSG
        await foreach (var nsg in nsgOpertions.ListNsgs()) {
            if (nsgOpertions.OkToDelete(regions, nsg.Data.Location!, nsg.Data.Name)) {
                if (nsg.Data.NetworkInterfaces.Count == 0 && nsg.Data.Subnets.Count == 0) {
                    await nsgOpertions.StartDeleteNsg(nsg.Data.Name);
                }
            }
        }
    }
}
