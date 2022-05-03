using Microsoft.Azure.Functions.Worker;

namespace Microsoft.OneFuzz.Service;


public partial class TimerProxy {
    private readonly ILogTracer _logger;

    private readonly IProxyOperations _proxYOperations;

    private readonly IScalesetOperations _scalesetOperations;

    private readonly INsgOperations _nsg;

    private readonly ICreds _creds;

    private readonly IConfigOperations _configOperations;

    private readonly ISubnet _subnet;

    public TimerProxy(ILogTracer logTracer, IProxyOperations proxies, IScalesetOperations scalesets, INsgOperations nsg, ICreds creds, IConfigOperations configOperations, ISubnet subnet) {
        _logger = logTracer;
        _proxYOperations = proxies;
        _scalesetOperations = scalesets;
        _nsg = nsg;
        _creds = creds;
        _configOperations = configOperations;
        _subnet = subnet;
    }

    //[Function("TimerProxy")]
    public async Async.Task Run([TimerTrigger("1.00:00:00")] TimerInfo myTimer) {
        var proxies = await _proxYOperations.QueryAsync().ToListAsync();

        foreach (var proxy in proxies) {
            if (VmStateHelper.Available().Contains(proxy.State)) {
                // Note, outdated checked at the start, but set at the end of this loop.
                // As this function is called via a timer, this works around a user
                // requesting to use the proxy while this function is checking if it's
                // out of date
                if (proxy.Outdated) {
                    await _proxYOperations.SetState(proxy, VmState.Stopping);
                    // If something is "wrong" with a proxy, delete & recreate it
                } else if (!_proxYOperations.IsAlive(proxy)) {
                    _logger.Error($"scaleset-proxy: alive check failed, stopping: {proxy.Region}");
                    await _proxYOperations.SetState(proxy, VmState.Stopping);
                } else {
                    await _proxYOperations.SaveProxyConfig(proxy);
                }
            }

            if (VmStateHelper.NeedsWork().Contains(proxy.State)) {
                _logger.Error($"scaleset-proxy: update state. proxy:{proxy.Region} state:{proxy.State}");
                await _proxYOperations.ProcessStateUpdate(proxy);
            }

            if (proxy.State != VmState.Stopped && _proxYOperations.IsOutdated(proxy)) {
                await _proxYOperations.Replace(proxy with { Outdated = true });
            }
        }

        // make sure there is a proxy for every currently active region
        var regions = await _scalesetOperations.QueryAsync().Select(x => x.Region).ToHashSetAsync();

        foreach (var region in regions) {
            var allOutdated = proxies.Where(x => x.Region == region).All(p => p.Outdated);
            if (allOutdated) {
                await _proxYOperations.GetOrCreate(region);
                _logger.Info($"Creating new proxy in region {region}");
            }

            // this is required in order to support upgrade from non-nsg to
            // nsg enabled OneFuzz this will overwrite existing NSG
            // assignment though. This behavior is acceptable at this point
            // since we do not support bring your own NSG

            if (await _nsg.GetNsg(region) != null) {
                var network = await Network.Create(region, _creds, _configOperations, _subnet);

                var subnet = await network.GetSubnet();
                var vnet = await network.GetVnet();
                if (subnet != null && vnet != null) {
                    var error = _nsg.AssociateSubnet(region, vnet, subnet);
                    if (error != null) {
                        _logger.Error($"Failed to associate NSG and subnet due to {error} in region {region}");
                    }
                }
            }

            // if there are NSGs with name same as the region that they are allocated
            // and have no NIC associated with it then delete the NSG
            await foreach (var nsg in _nsg.ListNsgs()) {
                if (_nsg.OkToDelete(regions, nsg.Data.Location, nsg.Data.Name)) {
                    if (nsg.Data.NetworkInterfaces.Count == 0 && nsg.Data.Subnets.Count == 0) {
                        await _nsg.StartDeleteNsg(nsg.Data.Name);
                    }
                }
            }
        }

    }
}
