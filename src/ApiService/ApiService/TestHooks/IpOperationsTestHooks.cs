using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service;


#if DEBUG
namespace ApiService.TestHooks {
    public class IpOperationsTestHooks {
        private readonly ILogTracer _log;
        private readonly IConfigOperations _configOps;
        private readonly IIpOperations _ipOps;

        public IpOperationsTestHooks(ILogTracer log, IConfigOperations configOps, IIpOperations ipOps) {
            _log = log.WithTag("TestHooks", nameof(IpOperationsTestHooks));
            _configOps = configOps;
            _ipOps = ipOps;
        }

        [Function("GetPublicNicTestHook")]
        public async Task<HttpResponseData> GetPublicNic([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/ipOps/publicNic")] HttpRequestData req) {
            _log.Info("Get public nic");

            var query = UriExtension.GetQueryComponents(req.Url);

            var rg = query["rg"];
            var name = query["name"];

            var nic = await _ipOps.GetPublicNic(rg, name);

            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(nic.Get().Value.Data.Name);
            return resp;
        }

        [Function("GetIpTestHook")]
        public async Task<HttpResponseData> GetIp([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/ipOps/ip")] HttpRequestData req) {
            _log.Info("Get public nic");

            var query = UriExtension.GetQueryComponents(req.Url);

            var rg = query["rg"];
            var name = query["name"];

            var ip = await _ipOps.GetIp(rg, name);

            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(ip.Get().Value.Data.Name);
            return resp;
        }


        [Function("DeletePublicNicTestHook")]
        public async Task<HttpResponseData> DeletePublicNic([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "testhooks/ipOps/publicNic")] HttpRequestData req) {
            _log.Info("Get public nic");

            var query = UriExtension.GetQueryComponents(req.Url);

            var rg = query["rg"];
            var name = query["name"];
            var yes = UriExtension.GetBool("force", query, false);

            if (yes) {
                await _ipOps.DeleteNic(rg, name);
                var resp = req.CreateResponse(HttpStatusCode.OK);
                return resp;
            } else {
                var resp = req.CreateResponse(HttpStatusCode.NotModified);
                return resp;
            }
        }

        [Function("DeleteIpTestHook")]
        public async Task<HttpResponseData> DeleteIp([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "testhooks/ipOps/ip")] HttpRequestData req) {
            _log.Info("Get public nic");

            var query = UriExtension.GetQueryComponents(req.Url);

            var rg = query["rg"];
            var name = query["name"];
            var yes = UriExtension.GetBool("force", query, false);

            if (yes) {
                await _ipOps.DeleteIp(rg, name);
                var resp = req.CreateResponse(HttpStatusCode.OK);
                return resp;
            } else {
                var resp = req.CreateResponse(HttpStatusCode.NotModified);
                return resp;
            }
        }
    }
}
#endif
