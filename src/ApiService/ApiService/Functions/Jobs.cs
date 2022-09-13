using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class Jobs {
    private readonly IOnefuzzContext _context;
    private readonly IEndpointAuthorization _auth;
    private readonly ILogTracer _logTracer;

    public Jobs(IEndpointAuthorization auth, IOnefuzzContext context, ILogTracer logTracer) {
        _context = context;
        _auth = auth;
        _logTracer = logTracer;
    }

    [Function("Jobs")]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET", "POST", "DELETE")] HttpRequestData req)
        => _auth.CallIfUser(req, r => r.Method switch {
            "GET" => Get(r),
            "DELETE" => Delete(r),
            "POST" => Post(r),
            var m => throw new NotSupportedException($"Unsupported HTTP method {m}"),
        });

    private async Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<JobCreate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "jobs create");
        }

        var userInfo = await _context.UserCredentials.ParseJwtToken(req);
        if (!userInfo.IsOk) {
            return await _context.RequestHandling.NotOk(req, userInfo.ErrorV, "jobs create");
        }

        var create = request.OkV;
        var cfg = new JobConfig(
            Build: create.Build,
            Duration: create.Duration,
            Logs: create.Logs,
            Name: create.Name,
            Project: create.Project);

        var job = new Job(
            JobId: Guid.NewGuid(),
            State: JobState.Init,
            Config: cfg) {
            UserInfo = userInfo.OkV,
        };

        // create the job logs container
        var metadata = new Dictionary<string, string>{
            { "container_type", "logs" }, // TODO: use ContainerType.Logs enum somehow; needs snake case name
        };
        var containerName = Container.Parse($"logs-{job.JobId}");
        var containerSas = await _context.Containers.CreateContainer(containerName, StorageType.Corpus, metadata);
        if (containerSas is null) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    Code: ErrorCode.UNABLE_TO_CREATE_CONTAINER,
                    Errors: new string[] { "unable to create logs container " }),
                "logs");
        }

        // log container must not have the SAS included
        var logContainerUri = new UriBuilder(containerSas) { Query = "" }.Uri;
        job = job with { Config = job.Config with { Logs = logContainerUri.ToString() } };
        await _context.JobOperations.Insert(job);
        return await RequestHandling.Ok(req, JobResponse.ForJob(job));
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
                new Error(
                    Code: ErrorCode.INVALID_JOB,
                    Errors: new string[] { "no such job" }),
                context: jobId.ToString());
        }

        if (job.State != JobState.Stopped && job.State != JobState.Stopping) {
            job = job with { State = JobState.Stopping };
            var r = await _context.JobOperations.Replace(job);
            if (!r.IsOk) {
                _logTracer.Error($"Failed to replace job {job.JobId} due to {r.ErrorV}");
            }
        }

        return await RequestHandling.Ok(req, JobResponse.ForJob(job));
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
                    new Error(
                        Code: ErrorCode.INVALID_JOB,
                        Errors: new string[] { "no such job" }),
                    context: jobId.ToString());
            }

            static JobTaskInfo TaskToJobTaskInfo(Task t) => new(t.TaskId, t.Config.Task.Type, t.State);

            // TODO: search.WithTasks is not checked in Python code?

            var taskInfo = await _context.TaskOperations.SearchStates(jobId).Select(TaskToJobTaskInfo).ToListAsync();
            job = job with { TaskInfo = taskInfo };
            return await RequestHandling.Ok(req, JobResponse.ForJob(job));
        }

        var jobs = await _context.JobOperations.SearchState(states: search.State ?? Enumerable.Empty<JobState>()).ToListAsync();
        return await RequestHandling.Ok(req, jobs.Select(j => JobResponse.ForJob(j)));
    }
}
