﻿using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;


public interface IProxyForwardOperations : IOrm<ProxyForward> {
    IAsyncEnumerable<ProxyForward> SearchForward(ScalesetId? scalesetId = null, Region? region = null, Guid? machineId = null, Guid? proxyId = null, int? dstPort = null);
    Forward ToForward(ProxyForward proxyForward);
    Task<OneFuzzResult<ProxyForward>> UpdateOrCreate(Region region, ScalesetId scalesetId, Guid machineId, int dstPort, int duration);
    Task<HashSet<Region>> RemoveForward(ScalesetId scalesetId, Guid? machineId = null, int? dstPort = null, Guid? proxyId = null);
}


public class ProxyForwardOperations : Orm<ProxyForward>, IProxyForwardOperations {
    private static readonly List<int> PORT_RANGES = Enumerable.Range(28000, 32000 - 28000).ToList();

    public ProxyForwardOperations(ILogger<ProxyForwardOperations> log, IOnefuzzContext context)
        : base(log, context) {

    }

    public IAsyncEnumerable<ProxyForward> SearchForward(ScalesetId? scalesetId = null, Region? region = null, Guid? machineId = null, Guid? proxyId = null, int? dstPort = null) {

        var conditions =
            new[] {
                scalesetId is not null ? Query.CreateQueryFilter($"scaleset_id eq {scalesetId}") : null,
                region is not null ? Query.CreateQueryFilter($"PartitionKey eq {region}") : null ,
                machineId is not null ? Query.CreateQueryFilter($"machine_id eq {machineId}") : null ,
                proxyId is not null ? Query.CreateQueryFilter($"proxy_id eq {proxyId}") : null ,
                dstPort is not null ? Query.CreateQueryFilter($"dst_port eq {dstPort}") : null ,
            }.Where(x => x != null);

        var filter = Query.And(conditions!);
        return QueryAsync(filter);
    }

    public Forward ToForward(ProxyForward proxyForward) {
        return new Forward(proxyForward.Port, proxyForward.DstPort, proxyForward.DstIp);
    }

    public async Task<OneFuzzResult<ProxyForward>> UpdateOrCreate(Region region, ScalesetId scalesetId, Guid machineId, int dstPort, int duration) {
        var privateIp = await _context.IpOperations.GetScalesetInstanceIp(scalesetId, machineId);

        if (privateIp == null) {
            return OneFuzzResult<ProxyForward>.Error(ErrorCode.UNABLE_TO_PORT_FORWARD, "no private ip for node");
        }

        var entries =
            await this.SearchForward(scalesetId: scalesetId, region: region, machineId: machineId, dstPort: dstPort).ToListAsync();

        var firstEntry = entries.FirstOrDefault();
        if (firstEntry != null) {
            var updated = firstEntry with { EndTime = DateTimeOffset.UtcNow + TimeSpan.FromHours(duration) };
            await Update(updated).IgnoreResult();
            return OneFuzzResult.Ok(updated);
        }

        var exisiting = entries.Select(x => x.Port).ToHashSet();

        foreach (var port in PORT_RANGES) {

            if (exisiting.Contains(port)) {
                continue;
            }

            var entry = new ProxyForward(
                Region: region,
                Port: port,
                ScalesetId: scalesetId,
                MachineId: machineId,
                DstIp: privateIp,
                DstPort: dstPort,
                EndTime: DateTimeOffset.UtcNow + TimeSpan.FromHours(duration),
                ProxyId: null
            );

            var result = await Replace(entry);
            if (!result.IsOk) {
                _logTracer.AddHttpStatus(result.ErrorV);
                _logTracer.LogInformation("port is already used {Entry}", entry);
            }

            return OneFuzzResult.Ok(entry);
        }

        return OneFuzzResult<ProxyForward>.Error(ErrorCode.UNABLE_TO_PORT_FORWARD, "all forward ports used");

    }

    public async Task<HashSet<Region>> RemoveForward(ScalesetId scalesetId, Guid? machineId, int? dstPort, Guid? proxyId) {
        var entries = await SearchForward(scalesetId: scalesetId, machineId: machineId, proxyId: proxyId, dstPort: dstPort).ToListAsync();

        var regions = new HashSet<Region>();
        foreach (var entry in entries) {
            _ = regions.Add(entry.Region);
            await Delete(entry).IgnoreResult();
        }

        return regions;
    }
}
