using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;


#if DEBUG
namespace ApiService.TestHooks {
    public class JobOperationsTestHooks {
        private readonly ILogTracer _log;
        private readonly IConfigOperations _configOps;
        private readonly IJobOperations _jobOps;

        public JobOperationsTestHooks(ILogTracer log, IConfigOperations configOps, IJobOperations jobOps) {
            _log = log.WithTag("TestHooks", nameof(JobOperationsTestHooks));
            _configOps = configOps;
            _jobOps = jobOps;
        }


        [Function("JobTestHook")]
        public async Task<HttpResponseData> GetJob([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/jobOps/job")] HttpRequestData req) {
            _log.Info("Get job info");

            var query = UriExtension.GetQueryComponents(req.Url);
            var jobId = Guid.Parse(query["jobId"]);

            var job = await _jobOps.Get(jobId);

            var msg = JsonSerializer.Serialize(job, EntityConverter.GetJsonSerializerOptions());
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(msg);
            return resp;
        }

        [Function("SearchExpiredTestHook")]
        public async Task<HttpResponseData> SearchExpired([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/jobOps/searchExpired")] HttpRequestData req) {
            _log.Info("Search expired jobs");

            var jobs = await _jobOps.SearchExpired().ToListAsync();

            var msg = JsonSerializer.Serialize(jobs, EntityConverter.GetJsonSerializerOptions());
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(msg);
            return resp;
        }

        [Function("SearchStateTestHook")]
        public async Task<HttpResponseData> SearchState([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/jobOps/searchState")] HttpRequestData req) {
            _log.Info("Search jobs by state");

            var query = UriExtension.GetQueryComponents(req.Url);
            var init = UriExtension.GetBoolValue("init", query, false);
            var enabled = UriExtension.GetBoolValue("enabled", query, false);
            var stopping = UriExtension.GetBoolValue("stopping", query, false);
            var stopped = UriExtension.GetBoolValue("stopped", query, false);

            var states = new List<JobState>();
            if (init) {
                states.Add(JobState.Init);
            }

            if (enabled) {
                states.Add(JobState.Enabled);
            }

            if (stopping) {
                states.Add(JobState.Stopping);
            }

            if (stopped) {
                states.Add(JobState.Stopped);
            }

            var jobs = await _jobOps.SearchState(states).ToListAsync();
            var msg = JsonSerializer.Serialize(jobs, EntityConverter.GetJsonSerializerOptions());
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(msg);
            return resp;
        }
    }
}
#endif
