using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;

#if DEBUG
namespace ApiService.TestHooks {
    public class ContainerTestHooks {

        private readonly ILogger _log;
        private readonly IConfigOperations _configOps;
        private readonly IContainers _containers;

        public ContainerTestHooks(ILogger<ContainerTestHooks> log, IConfigOperations configOps, IContainers containers) {
            _log = log;
            _log.AddTag("TestHooks", nameof(ContainerTestHooks));
            _configOps = configOps;
            _containers = containers;
        }

        [Function("GetInstanceIdTestHook")]
        public async Task<HttpResponseData> GetInstanceId([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/containers/instanceId")] HttpRequestData req) {
            _log.LogInformation("Get instance ID");
            var instanceId = await _containers.GetInstanceId();

            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(instanceId.ToString());
            return resp;
        }
    }
}
#endif
