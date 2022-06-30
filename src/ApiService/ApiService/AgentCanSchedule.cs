using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service;

public class AgentCanSchedule {
    private readonly ILogTracer _log;

    private readonly IOnefuzzContext _context;

    public AgentCanSchedule(ILogTracer log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    // [Function("AgentCanSchedule")]
    public async Async.Task<HttpResponseData> Run([HttpTrigger] HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<CanScheduleRequest>(req);
        if (!request.IsOk || request.OkV == null) {
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
                    }
                ),
                canScheduleRequest.MachineId.ToString()
            );
        }

        var allowed = true;
        var workStopped = false;

        if (!await _context.NodeOperations.CanProcessNewWork(node)) {
            allowed = false;
        }

        var task = await _context.TaskOperations.GetByTaskId(canScheduleRequest.TaskId);
        workStopped = task == null || task.State.ShuttingDown();

        if (allowed) {
            allowed = (await _context.NodeOperations.AcquireScaleInProtection(node)).IsOk;
        }

        return await RequestHandling.Ok(req, new CanSchedule(allowed, workStopped));
    }
}
