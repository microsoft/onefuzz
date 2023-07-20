using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;

#if DEBUG

namespace ApiService.TestHooks {
    public class CredsTestHooks {
        private readonly ILogger _log;
        private readonly IConfigOperations _configOps;
        private readonly ICreds _creds;

        public CredsTestHooks(ILogger<CredsTestHooks> log, IConfigOperations configOps, ICreds creds) {
            _log = log;
            _log.AddTag("TestHooks", nameof(CredsTestHooks));
            _configOps = configOps;
            _creds = creds;
        }

        [Function("GetSubscriptionTestHook")]
        public async Task<HttpResponseData> GetSubscription([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/creds/subscription")] HttpRequestData req) {
            _log.LogInformation($"Get subscription");
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(_creds.GetSubscription().ToString());
            return resp;
        }


        [Function("GetBaseResourceGroupTestHook")]
        public async Task<HttpResponseData> GetBaseResourceGroup([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/creds/baseResourceGroup")] HttpRequestData req) {
            _log.LogInformation("Get base resource group");
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(_creds.GetBaseResourceGroup().ToString());
            return resp;
        }

        [Function("GetInstanceNameTestHook")]
        public async Task<HttpResponseData> GetInstanceName([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/creds/instanceName")] HttpRequestData req) {
            _log.LogInformation("Get instance name");
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(_creds.GetInstanceName().ToString());
            return resp;
        }

        [Function("GetBaseRegionTestHook")]
        public async Task<HttpResponseData> GetBaseRegion([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/creds/baseRegion")] HttpRequestData req) {
            _log.LogInformation("Get base region");
            var resp = req.CreateResponse(HttpStatusCode.OK);
            var region = await _creds.GetBaseRegion();
            await resp.WriteStringAsync(region.String);
            return resp;
        }

        [Function("GetInstanceUrlTestHook")]
        public async Task<HttpResponseData> GetInstanceUrl([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/creds/instanceUrl")] HttpRequestData req) {
            _log.LogInformation("Get instance url");
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(_creds.GetInstanceUrl().ToString());
            return resp;
        }
    }
}
#endif
