using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

#if DEBUG
namespace ApiService.TestHooks {
    public class VmssTestHooks {

        private readonly ILogTracer _log;
        private readonly IConfigOperations _configOps;
        private readonly IVmssOperations _vmssOps;

        public VmssTestHooks(ILogTracer log, IConfigOperations configOps, IVmssOperations vmssOps) {
            _log = log.WithTag("TestHooks", nameof(VmssTestHooks));
            _configOps = configOps; ;
            _vmssOps = vmssOps; ;
        }


        [Function("ListInstanceIdsTesHook")]
        public async Task<HttpResponseData> ListInstanceIds([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/vmssOperations/listInstanceIds")] HttpRequestData req) {
            _log.Info("list instance ids");
            var query = UriExtension.GetQueryComponents(req.Url);
            var name = UriExtension.GetGuid("name", query) ?? throw new Exception("name must be set");
            var ids = await _vmssOps.ListInstanceIds(name);

            var json = JsonSerializer.Serialize(ids, EntityConverter.GetJsonSerializerOptions());
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(json);
            return resp;
        }

        [Function("GetInstanceIdsTesHook")]
        public async Task<HttpResponseData> GetInstanceId([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/vmssOperations/getInstanceId")] HttpRequestData req) {
            _log.Info("list instance ids");
            var query = UriExtension.GetQueryComponents(req.Url);
            var name = UriExtension.GetGuid("name", query) ?? throw new Exception("name must be set");
            var vmId = UriExtension.GetGuid("vmId", query) ?? throw new Exception("vmId must be set");
            var id = await _vmssOps.GetInstanceId(name, vmId);

            var json = JsonSerializer.Serialize(id, EntityConverter.GetJsonSerializerOptions());
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(json);
            return resp;
        }

        [Function("UpdateScaleInProtectionTestHook")]
        public async Task<HttpResponseData> UpdateScaleInProtection([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "testhooks/vmssOperations/updateScaleInProtection")] HttpRequestData req) {
            _log.Info("list instance ids");
            var query = UriExtension.GetQueryComponents(req.Url);
            var name = UriExtension.GetGuid("name", query) ?? throw new Exception("name must be set");
            var vmId = UriExtension.GetGuid("vmId", query) ?? throw new Exception("vmId must be set");
            var protectFromScaleIn = UriExtension.GetBool("protectFromScaleIn", query);
            var id = await _vmssOps.UpdateScaleInProtection(name, vmId, protectFromScaleIn);

            var json = JsonSerializer.Serialize(id, EntityConverter.GetJsonSerializerOptions());
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(json);
            return resp;
        }
    }
}

#endif
