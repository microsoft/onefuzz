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
    public class VmssTestHooks {

        private readonly ILogger _log;
        private readonly IConfigOperations _configOps;
        private readonly IVmssOperations _vmssOps;
        private readonly IScalesetOperations _scalesetOperations;

        public VmssTestHooks(ILogger<VmssTestHooks> log, IConfigOperations configOps, IVmssOperations vmssOps, IScalesetOperations scalesetOperations) {
            _log = log;
            _log.AddTag("TestHooks", nameof(VmssTestHooks));
            _configOps = configOps;
            _vmssOps = vmssOps;
            _scalesetOperations = scalesetOperations;
        }


        [Function("ListInstanceIdsTesHook")]
        public async Task<HttpResponseData> ListInstanceIds([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/vmssOperations/listInstanceIds")] HttpRequestData req) {
            _log.LogInformation("list instance ids");
            var query = UriExtension.GetQueryComponents(req.Url);
            var name = UriExtension.GetString("name", query) ?? throw new Exception("name must be set");
            var ids = await _vmssOps.ListInstanceIds(ScalesetId.Parse(name));

            var json = JsonSerializer.Serialize(ids, EntityConverter.GetJsonSerializerOptions());
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(json);
            return resp;
        }

        [Function("GetInstanceIdsTesHook")]
        public async Task<HttpResponseData> GetInstanceId([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/vmssOperations/getInstanceId")] HttpRequestData req) {
            _log.LogInformation("list instance ids");
            var query = UriExtension.GetQueryComponents(req.Url);
            var name = UriExtension.GetString("name", query) ?? throw new Exception("name must be set");
            var vmId = UriExtension.GetGuid("vmId", query) ?? throw new Exception("vmId must be set");
            var id = await _vmssOps.GetInstanceId(ScalesetId.Parse(name), vmId);

            var json = JsonSerializer.Serialize(id, EntityConverter.GetJsonSerializerOptions());
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(json);
            return resp;
        }

        [Function("UpdateScaleInProtectionTestHook")]
        public async Task<HttpResponseData> UpdateScaleInProtection([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "testhooks/vmssOperations/updateScaleInProtection")] HttpRequestData req) {
            _log.LogInformation("list instance ids");
            var query = UriExtension.GetQueryComponents(req.Url);
            var name = UriExtension.GetString("name", query) ?? throw new Exception("name must be set");
            var instanceId = UriExtension.GetString("instanceId", query) ?? throw new Exception("instanceId must be set");
            var scalesetResult = await _scalesetOperations.GetById(ScalesetId.Parse(name));
            if (!scalesetResult.IsOk) {
                throw new Exception("invalid scaleset name");
            }
            var protectFromScaleIn = UriExtension.GetBool("protectFromScaleIn", query);

            var id = await _vmssOps.UpdateScaleInProtection(scalesetResult.OkV, instanceId, protectFromScaleIn);

            var json = JsonSerializer.Serialize(id, EntityConverter.GetJsonSerializerOptions());
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(json);
            return resp;
        }
    }
}

#endif
