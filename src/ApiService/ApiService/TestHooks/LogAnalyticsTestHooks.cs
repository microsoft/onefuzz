using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;


#if DEBUG
namespace ApiService.TestHooks {
    public class LogAnalyticsTestHooks {
        private readonly ILogTracer _log;
        private readonly IConfigOperations _configOps;
        private readonly ILogAnalytics _logAnalytics;


        public LogAnalyticsTestHooks(ILogTracer log, IConfigOperations configOps, ILogAnalytics logAnalytics) {
            _log = log.WithTag("TestHooks", nameof(LogAnalyticsTestHooks));
            _configOps = configOps;
            _logAnalytics = logAnalytics;
        }

        [Function("MonitorSettingsTestHook")]
        public async Task<HttpResponseData> GetMonitorSettings([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/logAnalytics/monitorSettings")] HttpRequestData req) {
            _log.Info("Get monitor settings");

            var monitorSettings = await _logAnalytics.GetMonitorSettings();

            var msg = JsonSerializer.Serialize(monitorSettings, EntityConverter.GetJsonSerializerOptions());
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(msg);
            return resp;
        }

        [Function("WorkspaceIdTestHook")]
        public async Task<HttpResponseData> GetWorkspaceId([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/logAnalytics/workspaceId")] HttpRequestData req) {
            _log.Info("Get workspace id");

            var workspaceId = _logAnalytics.GetWorkspaceId();
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(workspaceId);
            return resp;
        }
    }
}
#endif
