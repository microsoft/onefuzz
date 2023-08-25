using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;


#if DEBUG
namespace ApiService.TestHooks {
    public class DiskOperationsTestHooks {
        private readonly ILogger _log;
        private readonly IConfigOperations _configOps;
        private readonly IDiskOperations _diskOps;
        private readonly ICreds _creds;

        public DiskOperationsTestHooks(ILogger<DiskOperationsTestHooks> log, IConfigOperations configOps, IDiskOperations diskOps, ICreds creds) {
            _log = log;
            _log.AddTag("TestHooks", nameof(DiskOperationsTestHooks));
            _configOps = configOps;
            _diskOps = diskOps;
            _creds = creds;
        }

        [Function("GetDiskNamesTestHook")]
        public async Task<HttpResponseData> GetSubscription([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/disks")] HttpRequestData req) {
            _log.LogInformation("Get disk names");
            var resp = req.CreateResponse(HttpStatusCode.OK);
            var diskNames = _diskOps.ListDisks(_creds.GetBaseResourceGroup()).ToList().Select(x => x.Data.Name);
            await resp.WriteAsJsonAsync(diskNames);
            return resp;
        }
    }
}
#endif
