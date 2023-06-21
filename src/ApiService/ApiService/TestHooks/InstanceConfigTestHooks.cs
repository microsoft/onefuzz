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
    public class InstanceConfigTestHooks {

        private readonly ILogger _log;
        private readonly IConfigOperations _configOps;

        public InstanceConfigTestHooks(ILogger<InstanceConfigTestHooks> log, IConfigOperations configOps) {
            _log = log;
            _log.AddTag("TestHooks", nameof(InstanceConfigTestHooks));
            _configOps = configOps;
        }

        [Function("GetInstanceConfigTestHook")]
        public async Task<HttpResponseData> Get([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/instance-config")] HttpRequestData req) {
            _log.LogInformation("Fetching instance config");
            var config = await _configOps.Fetch();

            if (config is null) {
                _log.LogError("Instance config is null");
                Error err = Error.Create(ErrorCode.INVALID_REQUEST, "Instance config is null");
                var resp = req.CreateResponse(HttpStatusCode.InternalServerError);
                await resp.WriteAsJsonAsync(err);
                return resp;
            } else {
                var str = EntityConverter.ToJsonString(config);

                var resp = req.CreateResponse(HttpStatusCode.OK);
                await resp.WriteStringAsync(str);
                return resp;
            }
        }

        [Function("PatchInstanceConfigTestHook")]
        public async Task<HttpResponseData> Patch([HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "testhooks/instance-config")] HttpRequestData req) {
            _log.LogInformation("Patch instance config");

            var s = await req.ReadAsStringAsync();
            var newInstanceConfig = JsonSerializer.Deserialize<InstanceConfig>(s!, EntityConverter.GetJsonSerializerOptions());

            if (newInstanceConfig is null) {
                var resp = req.CreateResponse();
                resp.StatusCode = HttpStatusCode.BadRequest;
                await resp.WriteAsJsonAsync(new { Error = "Instance config is not set" });
                return resp;
            } else {

                var query = UriExtension.GetQueryComponents(req.Url);
                bool isNew = UriExtension.GetBool("isNew", query, false);
                //requireEtag wont' work since our current schema does not return etag to the client when getting data form the table, so
                // there is no way to know which etag to use
                bool requireEtag = UriExtension.GetBool("requireEtag", query, false);

                await _configOps.Save(newInstanceConfig, isNew, requireEtag);

                var resp = req.CreateResponse();
                resp.StatusCode = HttpStatusCode.OK;
                return resp;
            }
        }
    }
}
#endif
