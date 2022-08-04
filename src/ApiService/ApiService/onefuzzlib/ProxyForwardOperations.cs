using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;


public interface IProxyForwardOperations : IOrm<ProxyForward> {
    IAsyncEnumerable<ProxyForward> SearchForward(Guid? scalesetId = null, string? region = null, Guid? machineId = null, Guid? proxyId = null, int? dstPort = null);
    Forward ToForward(ProxyForward proxyForward);
    Task<OneFuzzResult<ProxyForward>> UpdateOrCreate(string region, Guid scalesetId, Guid machineId, int dstPort, int duration);
    Task<HashSet<string>> RemoveForward(Guid scalesetId, Guid? machineId = null, int? dstPort = null, Guid? proxyId = null);
}


public class ProxyForwardOperations : Orm<ProxyForward>, IProxyForwardOperations {
    private static readonly List<int> PORT_RANGES = Enumerable.Range(28000, 32000 - 28000).ToList();

    public ProxyForwardOperations(ILogTracer log, IOnefuzzContext context)
        : base(log, context) {

    }

    public IAsyncEnumerable<ProxyForward> SearchForward(Guid? scalesetId = null, string? region = null, Guid? machineId = null, Guid? proxyId = null, int? dstPort = null) {

        var conditions =
            new[] {
                scalesetId != null ? $"scaleset_id eq '{scalesetId}'" : null,
                region != null ? $"PartitionKey eq '{region}'" : null ,
                machineId != null ? $"machine_id eq '{machineId}'" : null ,
                proxyId != null ? $"proxy_id eq '{proxyId}'" : null ,
                dstPort != null ? $"dst_port eq {dstPort}" : null ,
            }.Where(x => x != null);

        var filter = Query.And(conditions!);

        return QueryAsync(filter);
    }

    public Forward ToForward(ProxyForward proxyForward) {
        return new Forward(proxyForward.Port, proxyForward.DstPort, proxyForward.DstIp);
    }

    public async Task<OneFuzzResult<ProxyForward>> UpdateOrCreate(string region, Guid scalesetId, Guid machineId, int dstPort, int duration) {
        var privateIp = await _context.IpOperations.GetScalesetInstanceIp(scalesetId, machineId);

        if (privateIp == null) {
            return OneFuzzResult<ProxyForward>.Error(ErrorCode.UNABLE_TO_PORT_FORWARD, new[] { "no private ip for node" });
        }

        var entries =
            await this.SearchForward(scalesetId: scalesetId, region: region, machineId: machineId, dstPort: dstPort).ToListAsync();

        var firstEntry = entries.FirstOrDefault();
        if (firstEntry != null) {
            var updated = firstEntry with { EndTime = DateTimeOffset.UtcNow + TimeSpan.FromHours(duration) };
            await this.Update(updated);
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

            var result = await Insert(entry);
            if (!result.IsOk) {
                _logTracer.Info($"port is already used {entry}");
            }

            return OneFuzzResult.Ok(entry);
        }

        return OneFuzzResult<ProxyForward>.Error(ErrorCode.UNABLE_TO_PORT_FORWARD, new[] { "all forward ports used" });

    }

    public async Task<HashSet<string>> RemoveForward(Guid scalesetId, Guid? machineId, int? dstPort, Guid? proxyId) {
        var entries = await SearchForward(scalesetId: scalesetId, machineId: machineId, proxyId: proxyId, dstPort: dstPort).ToListAsync();

        var regions = new HashSet<string>();
        foreach (var entry in entries) {
            regions.Add(entry.Region);
            await Delete(entry);
        }

        return regions;
    }
}
