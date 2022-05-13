using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;


#if DEBUG
namespace ApiService.TestHooks {
    public class ProxyForwardTestHooks {
        private readonly ILogTracer _log;
        private readonly IConfigOperations _configOps;
        private readonly IProxyForwardOperations _proxyForward;

        public ProxyForwardTestHooks(ILogTracer log, IConfigOperations configOps, IProxyForwardOperations proxyForward) {
            _log = log.WithTag("TestHooks", nameof(ProxyForwardTestHooks));
            _configOps = configOps; ;
            _proxyForward = proxyForward; ;
        }

        [Function("SearchForwardTestHook")]
        public async Task<HttpResponseData> SearchForward([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/proxyForwardOperations/search")] HttpRequestData req) {
            _log.Info("search proxy forward");

            var query = UriExtension.GetQueryComponents(req.Url);

            var poolRes = _proxyForward.SearchForward(
                UriExtension.GetGuid("scaleSetId", query),
                UriExtension.GetString("region", query),
                UriExtension.GetGuid("machineId", query),
                UriExtension.GetGuid("proxyId", query),
                UriExtension.GetInt("dstPort", query));

            var json = JsonSerializer.Serialize(await poolRes.ToListAsync(), EntityConverter.GetJsonSerializerOptions());
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(json);
            return resp;
        }
    }
}
#endif
