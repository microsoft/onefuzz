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
            _log.Warning($"Cannot schedule due to {request.ErrorV}");
            return await _context.RequestHandling.NotOk(req, request.ErrorV, typeof(CanScheduleRequest).ToString());
        }

        var canScheduleRequest = request.OkV;

        var node = await _context.NodeOperations.GetByMachineId(canScheduleRequest.MachineId);
        if (node == null) {
            _log.Warning($"Unable to find {canScheduleRequest.MachineId:Tag:MachineId}");
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(ErrorCode.UNABLE_TO_FIND, "unable to find node"),
                canScheduleRequest.MachineId.ToString());
        }


        var canProcessNewWork = await _context.NodeOperations.CanProcessNewWork(node);
        var allowed = canProcessNewWork.IsAllowed;
        var reason = canProcessNewWork.Reason;

        var task = await _context.TaskOperations.GetByTaskId(canScheduleRequest.TaskId);
        var workStopped = task == null || task.State.ShuttingDown();
        if (!allowed) {
            _log.Info($"Node cannot process new work {node.PoolName:Tag:PoolName} {node.ScalesetId:Tag:ScalesetId} - {node.MachineId:Tag:MachineId} ");
            return await new RequestHandling(_log).Ok(req, new CanSchedule(Allowed: allowed, WorkStopped: workStopped, Reason: reason));
        }

        if (workStopped) {
            _log.Info($"Work stopped for: {canScheduleRequest.MachineId:Tag:MachineId} and {canScheduleRequest.TaskId:Tag:TaskId}");
            return await new RequestHandling(_log).Ok(req, new CanSchedule(Allowed: false, WorkStopped: workStopped, Reason: "Work stopped"));
        }

        var scp = await _context.NodeOperations.AcquireScaleInProtection(node);
        if (!scp.IsOk) {
            _log.Warning($"Failed to acquire scale in protection for: {node.MachineId:Tag:MachineId} in: {node.PoolName:Tag:PoolName} due to {scp.ErrorV:Tag:Error}");
        }
        _ = scp.OkV; // node could be updated but we don't use it after this
        allowed = scp.IsOk;
        return await new RequestHandling(_log).Ok(req, new CanSchedule(Allowed: allowed, WorkStopped: workStopped, Reason: reason));
    }
}
