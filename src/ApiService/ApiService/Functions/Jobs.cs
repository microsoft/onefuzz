using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.Auth;
namespace Microsoft.OneFuzz.Service.Functions;

public class Jobs {
    private readonly IOnefuzzContext _context;
    private readonly ILogger _logTracer;

    public Jobs(IOnefuzzContext context, ILogger<Jobs> logTracer) {
        _context = context;
        _logTracer = logTracer;
    }

    [Function("Jobs")]
    [Authorize(Allow.User)]
    public Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "GET", "POST", "DELETE")]
        HttpRequestData req,
        FunctionContext context)
        => req.Method switch {
            "GET" => Get(req),
            "DELETE" => Delete(req),
            "POST" => Post(req, context),
            var m => throw new NotSupportedException($"Unsupported HTTP method {m}"),
        };

    private async Task<HttpResponseData> Post(HttpRequestData req, FunctionContext context) {
        var request = await RequestHandling.ParseRequest<JobCreate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "jobs create");
        }

        var userInfo = context.GetUserAuthInfo();

        var create = request.OkV;

        var job = new Job(
            JobId: Guid.NewGuid(),
            State: JobState.Init,
            Config: new(
                Build: create.Build,
                Duration: create.Duration,
                Logs: create.Logs,
                Name: create.Name,
                Project: create.Project),
            UserInfo: new(
                ObjectId: userInfo.UserInfo.ObjectId,
                ApplicationId: userInfo.UserInfo.ApplicationId));

        // create the job logs container
        var metadata = new Dictionary<string, string>{
            { "container_type", "logs" }, // TODO: use ContainerType.Logs enum somehow; needs snake case name
        };

        var containerSas = await _context.Containers.CreateNewContainer(
            Container.Parse($"logs-{job.JobId}"),
            StorageType.Corpus,
            metadata);

        if (containerSas is null) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(ErrorCode.UNABLE_TO_CREATE_CONTAINER, "unable to create logs container "),
                "logs");
        }

        // log container must not have the SAS included
        var logContainerUri = new UriBuilder(containerSas) { Query = "" }.Uri;
        job = job with { Config = job.Config with { Logs = logContainerUri.ToString() } };
        var r = await _context.JobOperations.Insert(job);
        if (!r.IsOk) {
            _logTracer.AddTag("HttpRequest", "POST");
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("failed to insert job {JobId}", job.JobId);
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(
                    ErrorCode.UNABLE_TO_CREATE,
                    "unable to create job"),
                "job");
        }

        await _context.Events.SendEvent(new EventJobCreated(job.JobId, job.Config, job.UserInfo));
        return await RequestHandling.Ok(req, JobResponse.ForJob(job, taskInfo: null));
    }

    private async Task<HttpResponseData> Delete(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<JobGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "jobs delete");
        }

        var jobId = request.OkV.JobId;
        var job = await _context.JobOperations.Get(jobId);
        if (job is null) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(
                    ErrorCode.INVALID_JOB,
                    "no such job"),
                context: jobId.ToString());
        }

        if (job.State != JobState.Stopped && job.State != JobState.Stopping) {
            job = job with { State = JobState.Stopping };
            var r = await _context.JobOperations.Replace(job);
            if (!r.IsOk) {
                _logTracer.AddTag("HttpRequest", "DELETE");
                _logTracer.AddHttpStatus(r.ErrorV);
                _logTracer.LogError("Failed to replace job {JobId}", job.JobId);
            }
        }

        return await RequestHandling.Ok(req, JobResponse.ForJob(job, taskInfo: null));
    }

    private async Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<JobSearch>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "jobs");
        }

        var search = request.OkV;
        if (search.JobId is Guid jobId) {
            var job = await _context.JobOperations.Get(jobId);
            if (job is null) {
                return await _context.RequestHandling.NotOk(
                    req,
                    Error.Create(ErrorCode.INVALID_JOB, "no such job"),
                    context: jobId.ToString());
            }

            static JobTaskInfo TaskToJobTaskInfo(Task t) => new(t.TaskId, t.Config.Task.Type, t.State);

            var tasks = _context.TaskOperations.SearchStates(jobId);
            if (search.WithTasks ?? false) {
                var ts = await tasks.ToListAsync();
                return await RequestHandling.Ok(req, JobResponse.ForJob(job, ts));
            } else {
                var taskInfo = await tasks.Select(TaskToJobTaskInfo).ToListAsync();
                return await RequestHandling.Ok(req, JobResponse.ForJob(job, taskInfo));
            }
        }

        var jobs = await _context.JobOperations.SearchState(states: search.State ?? Enumerable.Empty<JobState>()).ToListAsync();
        return await RequestHandling.Ok(req, jobs.Select(j => JobResponse.ForJob(j, taskInfo: null)));
    }
}
