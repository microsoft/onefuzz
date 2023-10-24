using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.Auth;
namespace Microsoft.OneFuzz.Service.Functions;

public class AgentCanSchedule {
    private readonly ILogger _log;
    private readonly IOnefuzzContext _context;

    public AgentCanSchedule(ILogger<AgentCanSchedule> log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("AgentCanSchedule")]
    [Authorize(Allow.Agent)]
    public async Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "POST", Route="agents/can_schedule")]
        HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<CanScheduleRequest>(req);
        if (!request.IsOk) {
            _log.LogWarning("Cannot schedule due to {error}", request.ErrorV);
            return await _context.RequestHandling.NotOk(req, request.ErrorV, typeof(CanScheduleRequest).ToString());
        }

        var canScheduleRequest = request.OkV;

        var node = await _context.NodeOperations.GetByMachineId(canScheduleRequest.MachineId);
        if (node == null) {
            _log.LogWarning("Unable to find {MachineId}", canScheduleRequest.MachineId);
            return await _context.RequestHandling.NotOk(
                req,
                Error.Create(ErrorCode.UNABLE_TO_FIND, "unable to find node"),
                canScheduleRequest.MachineId.ToString());
        }


        var canProcessNewWork = await _context.NodeOperations.CanProcessNewWork(node);
        var allowed = canProcessNewWork.IsAllowed;
        var reason = canProcessNewWork.Reason;

        var task = await
            (canScheduleRequest.JobId.HasValue
            ? _context.TaskOperations.GetByJobIdAndTaskId(canScheduleRequest.JobId.Value, canScheduleRequest.TaskId)
            // old agent, fall back
            : _context.TaskOperations.GetByTaskIdSlow(canScheduleRequest.TaskId));

        var workStopped = task == null || task.State.ShuttingDown();
        if (!allowed) {
            _log.LogInformation("Node cannot process new work {PoolName} {ScalesetId} - {MachineId} ", node.PoolName, node.ScalesetId, node.MachineId);
            return await RequestHandling.Ok(req, new CanSchedule(Allowed: allowed, WorkStopped: workStopped, Reason: reason));
        }

        if (workStopped) {
            _log.LogInformation("Work stopped for: {MachineId} and {TaskId}", canScheduleRequest.MachineId, canScheduleRequest.TaskId);
            return await RequestHandling.Ok(req, new CanSchedule(Allowed: false, WorkStopped: workStopped, Reason: "Work stopped"));
        }

        var scp = await _context.NodeOperations.AcquireScaleInProtection(node);
        if (!scp.IsOk) {
            _log.LogWarning("Failed to acquire scale in protection for: {MachineId} in: {PoolName} due to {Error}", node.MachineId, node.PoolName, scp.ErrorV);
        }
        _ = scp.OkV; // node could be updated but we don't use it after this
        allowed = scp.IsOk;

        if (allowed) {
            using (_log.BeginScope("TaskAllowedToSchedule")) {
                _log.AddTags(
                    new Dictionary<string, string> {
                        {"MachineId", node.MachineId.ToString()},
                        {"TaskId", task is not null ? task.TaskId.ToString() : string.Empty} }
                    );
                _log.LogMetric("TaskAllowedToSchedule", 1);
            }
        }
        return await RequestHandling.Ok(req, new CanSchedule(Allowed: allowed, WorkStopped: workStopped, Reason: reason));
    }
}
