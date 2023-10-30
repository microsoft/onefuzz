using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.Auth;

namespace Microsoft.OneFuzz.Service.Functions;

public class Tasks {
    private readonly IOnefuzzContext _context;

    public Tasks(IOnefuzzContext context) {
        _context = context;
    }

    [Function("Tasks")]
    [Authorize(Allow.User)]
    public Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "GET", "POST", "DELETE")]
        HttpRequestData req,
        FunctionContext context)
        => req.Method switch {
            "GET" => Get(req),
            "POST" => Post(req, context),
            "DELETE" => Delete(req),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        };

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<TaskSearch>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "task get");
        }

        if (request.OkV.TaskId is Guid taskId) {
            var task = await _context.TaskOperations.GetByTaskIdSlow(taskId);
            if (task == null) {
                return await _context.RequestHandling.NotOk(
                    req,
                    Error.Create(
                        ErrorCode.INVALID_REQUEST,
                        "unable to find task"),
                    "task get");
            }

            var (nodes, events) = await (
                _context.NodeTasksOperations.GetNodeAssignments(taskId).ToListAsync().AsTask(),
                _context.TaskEventOperations.GetSummary(taskId).ToListAsync().AsTask());

            var auth = task.Auth == null ? null : await _context.SecretsOperations.GetSecretValue(task.Auth);

            var result = new TaskSearchResult(
                JobId: task.JobId,
                TaskId: task.TaskId,
                State: task.State,
                Os: task.Os,
                Config: task.Config,
                Error: task.Error,
                Auth: auth,
                Heartbeat: task.Heartbeat,
                EndTime: task.EndTime,
                UserInfo: task.UserInfo,
                Nodes: nodes,
                Events: events,
                Timestamp: task.Timestamp);

            return await RequestHandling.Ok(req, result);
        }

        var tasks = await _context.TaskOperations.SearchStates(request.OkV.JobId, request.OkV.State).ToListAsync();
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(tasks);
        return response;
    }


    private async Async.Task<HttpResponseData> Post(HttpRequestData req, FunctionContext context) {
        var request = await RequestHandling.ParseRequest<TaskCreate>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "task create");
        }

        var userInfo = context.GetUserAuthInfo();

        var create = request.OkV;
        var cfg = new TaskConfig(
            JobId: create.JobId,
            PrereqTasks: create.PrereqTasks,
            Task: create.Task,
            Vm: null,
            Pool: create.Pool,
            Containers: create.Containers,
            Tags: create.Tags,
            Debug: create.Debug,
            Colocate: create.Colocate);

        var checkConfig = await _context.Config.CheckConfig(cfg);
        if (!checkConfig.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(ErrorCode.INVALID_REQUEST, checkConfig.ErrorV.Error),
                "task create");
        }

        if (System.Web.HttpUtility.ParseQueryString(req.Url.Query)["dryrun"] != null) {
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new BoolResult(true));
            return response;
        }

        var job = await _context.JobOperations.Get(cfg.JobId);
        if (job == null) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(ErrorCode.INVALID_REQUEST, "unable to find job"),
                cfg.JobId.ToString());
        }

        if (job.State != JobState.Enabled && job.State != JobState.Init) {
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(ErrorCode.UNABLE_TO_ADD_TASK_TO_JOB, $"unable to add a job in state {job.State}"),
                cfg.JobId.ToString());
        }

        if (cfg.PrereqTasks != null) {
            foreach (var taskId in cfg.PrereqTasks) {
                var prereq = await _context.TaskOperations.GetByJobIdAndTaskId(cfg.JobId, taskId);
                if (prereq == null) {
                    return await _context.RequestHandling.NotOk(
                        req,
                        Error.Create(ErrorCode.INVALID_REQUEST, "unable to find task "),
                        "task create prerequisite");
                }
            }
        }

        var task = await _context.TaskOperations.Create(cfg, cfg.JobId, userInfo.UserInfo);

        if (!task.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                task.ErrorV,
                "task create invalid pool");
        }

        var taskResponse = req.CreateResponse(HttpStatusCode.OK);
        await taskResponse.WriteAsJsonAsync(task.OkV);
        return taskResponse;
    }

    private async Async.Task<HttpResponseData> Delete(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<TaskGet>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                context: "task delete");
        }


        var task = await _context.TaskOperations.GetByTaskIdSlow(request.OkV.TaskId);
        if (task == null) {
            return await _context.RequestHandling.NotOk(req, Error.Create(ErrorCode.INVALID_REQUEST, "unable to find task"
            ), "task delete");

        }

        await _context.TaskOperations.MarkStopping(task, "task is deleted");

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(task);
        return response;
    }
}
