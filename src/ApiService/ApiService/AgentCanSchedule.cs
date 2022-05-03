using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service;

public class AgentCanSchedule {
    private readonly ILogTracer _log;

    private readonly IStorage _storage;

    private readonly INodeOperations _nodeOperations;

    private readonly ITaskOperations _taskOperations;

    private readonly IScalesetOperations _scalesetOperations;

    public AgentCanSchedule(ILogTracer log, IStorage storage, INodeOperations nodeOperations, ITaskOperations taskOperations, IScalesetOperations scalesetOperations) {
        _log = log;
        _storage = storage;
        _nodeOperations = nodeOperations;
        _taskOperations = taskOperations;
        _scalesetOperations = scalesetOperations;
    }

    [Function("AgentCanSchedule")]
    public async Async.Task<HttpResponseData> Run([HttpTrigger] HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<CanScheduleRequest>(req);
        if (!request.IsOk || request.OkV == null) {
            return await RequestHandling.NotOk(req, request.ErrorV, typeof(CanScheduleRequest).ToString(), _log);
        }

        var canScheduleRequest = request.OkV;

        var node = await _nodeOperations.GetByMachineId(canScheduleRequest.MachineId);
        if (node == null) {
            return await RequestHandling.NotOk(
                req,
                new Error(
                    ErrorCode.UNABLE_TO_FIND,
                    new string[] {
                        "unable to find node"
                    }
                ),
                canScheduleRequest.MachineId.ToString(),
                _log
            );
        }

        var allowed = true;
        var workStopped = false;

        if (!await _nodeOperations.CanProcessNewWork(node)) {
            allowed = false;
        }

        var task = await _taskOperations.GetByTaskId(canScheduleRequest.TaskId);
        workStopped = task == null || TaskStateHelper.ShuttingDown().Contains(task.State);

        if (allowed) {
            allowed = (await _nodeOperations.AcquireScaleInProtection(node)).IsOk;
        }

        return await RequestHandling.Ok(
            req,
            new BaseResponse[] {
            new CanSchedule(allowed, workStopped)
        });
    }
}
