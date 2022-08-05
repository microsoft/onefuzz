using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class AgentCanSchedule {
    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public AgentCanSchedule(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    [Function("AgentCanSchedule")]
    public Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "POST", Route="agents/can_schedule")]
        HttpRequestData req)
        => _auth.CallIfAgent(req, Post);

    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<CanScheduleRequest>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, typeof(CanScheduleRequest).ToString());
        }

        var canScheduleRequest = request.OkV;

        var node = await _context.NodeOperations.GetByMachineId(canScheduleRequest.MachineId);
        if (node == null) {
            return await _context.RequestHandling.NotOk(
                req,
                new Error(
                    ErrorCode.UNABLE_TO_FIND,
                    new string[] {
                        "unable to find node"
                    }),
                canScheduleRequest.MachineId.ToString());
        }

        var allowed = true;

        if (!await _context.NodeOperations.CanProcessNewWork(node)) {
            allowed = false;
        }

        var task = await _context.TaskOperations.GetByTaskId(canScheduleRequest.TaskId);
        var workStopped = task == null || task.State.ShuttingDown();

        if (workStopped) {
            allowed = false;
        }

        if (allowed) {
            allowed = (await _context.NodeOperations.AcquireScaleInProtection(node)).IsOk;
        }

        return await RequestHandling.Ok(req, new CanSchedule(Allowed: allowed, WorkStopped: workStopped));
    }
}
