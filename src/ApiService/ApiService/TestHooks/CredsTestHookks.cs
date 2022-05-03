using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

using Microsoft.OneFuzz.Service;

#if DEBUG

namespace ApiService.TestHooks {
    public class CredsTestHookks {
        private readonly ILogTracer _log;
        private readonly IConfigOperations _configOps;
        private readonly ICreds _creds;

        public CredsTestHookks(ILogTracer log, IConfigOperations configOps, ICreds creds) {
            _log = log.WithTag("TestHooks", nameof(CredsTestHookks));
            _configOps = configOps;
            _creds = creds;
        }

        [Function("GetSubscriptionTestHook")]
        public async Task<HttpResponseData> GetSubscription([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/creds/subscription")] HttpRequestData req) {
            _log.Info("Get subscription");
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(_creds.GetSubscription().ToString());
            return resp;
        }


        [Function("GetBaseResourceGroupTestHook")]
        public async Task<HttpResponseData> GetBaseResourceGroup([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/creds/baseResourceGroup")] HttpRequestData req) {
            _log.Info("Get base resource group");
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(_creds.GetBaseResourceGroup().ToString());
            return resp;
        }

        [Function("GetInstanceNameTestHook")]
        public async Task<HttpResponseData> GetInstanceName([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/creds/instanceName")] HttpRequestData req) {
            _log.Info("Get instance name");
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(_creds.GetInstanceName().ToString());
            return resp;
        }

        [Function("GetBaseRegionTestHook")]
        public async Task<HttpResponseData> GetBaseRegion([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/creds/baseRegion")] HttpRequestData req) {
            _log.Info("Get base region");
            var resp = req.CreateResponse(HttpStatusCode.OK);
            var region = await _creds.GetBaseRegion();
            await resp.WriteStringAsync(region);
            return resp;
        }

        [Function("GetInstanceUrlTestHook")]
        public async Task<HttpResponseData> GetInstanceUrl([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/creds/instanceUrl")] HttpRequestData req) {
            _log.Info("Get instance url");
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(_creds.GetInstanceUrl().ToString());
            return resp;
        }

    }
}

#endif
