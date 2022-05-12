using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service;


#if DEBUG

namespace ApiService.TestHooks {
    public class NsgOperationsTestHooks {

        private readonly ILogTracer _log;
        private readonly IConfigOperations _configOps;
        private readonly INsgOperations _nsgOperations;

        public NsgOperationsTestHooks(ILogTracer log, IConfigOperations configOps, INsgOperations nsgOperations) {
            _log = log.WithTag("TestHooks", nameof(NsgOperationsTestHooks));
            _configOps = configOps; ;
            _nsgOperations = nsgOperations;
        }


        [Function("GetNsgTestHook")]
        public async Task<HttpResponseData> GetNsg([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/nsgOperations/nsg")] HttpRequestData req) {
            _log.Info("get nsg");

            var query = UriExtension.GetQueryComponents(req.Url);
            var nsg = await _nsgOperations.GetNsg(query["name"]);

            if (nsg is null) {
                var resp = req.CreateResponse(HttpStatusCode.NotFound);
                return resp;
            } else {
                var resp = req.CreateResponse(HttpStatusCode.OK);
                var data = nsg!.Data;
                await resp.WriteAsJsonAsync(new { ResourceId = data.ResourceGuid });
                return resp;
            }
        }

        [Function("ListNsgsTestHook")]
        public async Task<HttpResponseData> ListNsgs([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/nsgOperations/listNsgs")] HttpRequestData req) {
            _log.Info("list nsgs");

            var nsgs = await _nsgOperations.ListNsgs().ToListAsync();

            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(nsgs.Select(x => new { Name = x.Data.Name, ResourceId = x.Data.ResourceGuid }));
            return resp;
        }


        [Function("DeleteNsgTestHook")]
        public async Task<HttpResponseData> DeleteNsg([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "testhooks/nsgOperations/nsg")] HttpRequestData req) {
            _log.Info("delete nsgs");

            var query = UriExtension.GetQueryComponents(req.Url);
            var name = query["name"];
            var deleted = await _nsgOperations.StartDeleteNsg(name);

            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(new { Name = name, Deleted = deleted });
            return resp;
        }
    }
}
#endif
