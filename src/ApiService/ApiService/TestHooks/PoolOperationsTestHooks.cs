using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
#if DEBUG
namespace ApiService.TestHooks {
    public class PoolOperationsTestHooks {
        private readonly ILogger _log;
        private readonly IConfigOperations _configOps;
        private readonly IPoolOperations _poolOps;

        public PoolOperationsTestHooks(ILogger<PoolOperationsTestHooks> log, IConfigOperations configOps, IPoolOperations poolOps) {
            _log = log;
            _log.AddTag("TestHooks", nameof(PoolOperationsTestHooks));
            _configOps = configOps; ;
            _poolOps = poolOps;
        }


        [Function("GetPoolTestHook")]
        public async Task<HttpResponseData> GetPool([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/poolOperations/pool")] HttpRequestData req) {
            _log.LogInformation("get pool");

            var query = UriExtension.GetQueryComponents(req.Url);
            var poolRes = await _poolOps.GetByName(PoolName.Parse(query["name"]));

            if (poolRes.IsOk) {
                var resp = req.CreateResponse(HttpStatusCode.OK);
                var data = poolRes.OkV;
                var msg = JsonSerializer.Serialize(data, EntityConverter.GetJsonSerializerOptions());
                await resp.WriteStringAsync(msg);
                return resp;
            } else {
                var resp = req.CreateResponse(HttpStatusCode.BadRequest);
                var msg = JsonSerializer.Serialize(poolRes.ErrorV, EntityConverter.GetJsonSerializerOptions());
                await resp.WriteStringAsync(msg);
                return resp;
            }
        }
    }
}

#endif
