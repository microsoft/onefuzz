using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class Tasks {
    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public Tasks(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    [Function("Tasks")]
    public Async.Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "GET", "POST", "DELETE")] HttpRequestData req) {
        return _auth.CallIfUser(req, r => r.Method switch {
            "GET" => Get(r),
            "POST" => Post(r),
            "DELETE" => Delete(r),
            _ => throw new InvalidOperationException("Unsupported HTTP method"),
        });
    }

    private async Async.Task<HttpResponseData> Get(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<TaskSearch>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "task get");
        }

        if (request.OkV.TaskId != null) {
            var task = await _context.TaskOperations.GetByTaskId(request.OkV.TaskId.Value);
            if (task == null) {
                return await _context.RequestHandling.NotOk(req, new Error(ErrorCode.INVALID_REQUEST, new[] { "unable to find task"
                }), "task get");

            }
            task.Nodes = await _context.NodeTasksOperations.GetNodeAssignments(request.OkV.TaskId.Value).ToListAsync();
            task.Events = await _context.TaskEventOperations.GetSummary(request.OkV.TaskId.Value).ToListAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(task);
            return response;

        }

        var tasks = await _context.TaskOperations.SearchAll().ToListAsync();
        var response2 = req.CreateResponse(HttpStatusCode.OK);
        await response2.WriteAsJsonAsync(tasks);
        return response2;
    }


    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<TaskConfig>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "task create");
        }

        var userInfo = await _context.UserCredentials.ParseJwtToken(req);
        if (!userInfo.IsOk) {
            return await _context.RequestHandling.NotOk(req, userInfo.ErrorV, "task create");
        }

        var checkConfig = await _context.Config.CheckConfig(request.OkV);
        if (!checkConfig.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(ErrorCode.INVALID_REQUEST, new[] { checkConfig.ErrorV.Error }),
                "task create");
        }

        if (System.Web.HttpUtility.ParseQueryString(req.Url.Query)["dryrun"] != null) {
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new BoolResult(true));
            return response;
        }

        var job = await _context.JobOperations.Get(request.OkV.JobId);
        if (job == null) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(ErrorCode.INVALID_REQUEST, new[] { "unable to find job" }),
                request.OkV.JobId.ToString());
        }

        if (job.State != JobState.Enabled && job.State != JobState.Init) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(ErrorCode.UNABLE_TO_ADD_TASK_TO_JOB, new[] { $"unable to add a job in state {job.State}" }),
                request.OkV.JobId.ToString());
        }

        if (request.OkV.PrereqTasks != null) {
            foreach (var taskId in request.OkV.PrereqTasks) {
                var prereq = await _context.TaskOperations.GetByTaskId(taskId);

                if (prereq == null) {
                    return await _context.RequestHandling.NotOk(
                        req,
                        new Error(ErrorCode.INVALID_REQUEST, new[] { "unable to find task " }),
                        "task create prerequisite");
                }
            }
        }

        var task = await _context.TaskOperations.Create(request.OkV, request.OkV.JobId, userInfo.OkV);

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


        var task = await _context.TaskOperations.GetByTaskId(request.OkV.TaskId);
        if (task == null) {
            return await _context.RequestHandling.NotOk(req, new Error(ErrorCode.INVALID_REQUEST, new[] { "unable to find task"
            }), "task delete");

        }

        await _context.TaskOperations.MarkStopping(task);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(task);
        return response;
    }
}
