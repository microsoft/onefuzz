using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

using Microsoft.OneFuzz.Service;


namespace ApiService.TestHooks {
    public class ContainerTestHooks {

        private readonly ILogTracer _log;
        private readonly IConfigOperations _configOps;
        private readonly IContainers _containers;

        public ContainerTestHooks(ILogTracer log, IConfigOperations configOps, IContainers containers) {
            _log = log.WithTag("TestHooks", "ContainerTestHooks");
            _configOps = configOps;
            _containers = containers;
        }

        [Function("GetInstanceId")]
        public async Task<HttpResponseData> GetInstanceId([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/containers/instanceId")] HttpRequestData req) {
            _log.Info("Get instance ID");
            var instanceId = await _containers.GetInstanceId();

            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(instanceId.ToString());
            return resp;
        }



    }
}
