using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;


public interface IProxyForwardOperations : IOrm<ProxyForward> {
    IAsyncEnumerable<ProxyForward> SearchForward(Guid? scalesetId = null, string? region = null, Guid? machineId = null, Guid? proxyId = null, int? dstPort = null);
}


public class ProxyForwardOperations : Orm<ProxyForward>, IProxyForwardOperations {
    public ProxyForwardOperations(ILogTracer log, IOnefuzzContext context)
        : base(log, context) {

        }

    public IAsyncEnumerable<ProxyForward> SearchForward(Guid? scalesetId = null, string? region = null, Guid? machineId = null, Guid? proxyId = null, int? dstPort = null) {

        var conditions =
            new[] {
                scalesetId != null ? $"scaleset_id eq '{scalesetId}'" : null,
                region != null ? $"region eq '{region}'" : null ,
                machineId != null ? $"machine_id eq '{machineId}'" : null ,
                proxyId != null ? $"proxy_id eq '{proxyId}'" : null ,
                dstPort != null ? $"dsp_port eq {dstPort }" : null ,
            }.Where(x => x != null);

        var filter = string.Join(" and ", conditions);

        return QueryAsync(filter);
    }
}
