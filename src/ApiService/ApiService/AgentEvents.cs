using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service;

public class AgentEvents {
    private readonly ILogTracer _log;

    private readonly IOnefuzzContext _context;

    public AgentEvents(ILogTracer log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    // [Function("AgentEvents")]
    public async Async.Task<HttpResponseData> Run([HttpTrigger("post")] HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<NodeStateEnvelope>(req);
        if (!request.IsOk || request.OkV == null) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, nameof(NodeStateEnvelope));
        }

        var envelope = request.OkV;

        try {
            switch (envelope.Event) {
                case NodeStateUpdate updateEvent: {
                        await OnStateUpdate(envelope.MachineId, updateEvent);
                        return await RequestHandling.Ok(req, Enumerable.Empty<BaseResponse>());
                    }
                case WorkerEvent workerEvent: {
                        await OnWorkerEvent(envelope.MachineId, workerEvent);
                        return await RequestHandling.Ok(req, Enumerable.Empty<BaseResponse>());
                    }
                case NodeEvent nodeEvent: {
                        if (nodeEvent.StateUpdate is not null) {
                            await OnStateUpdate(envelope.MachineId, nodeEvent.StateUpdate);
                        }

                        if (nodeEvent.WorkerEvent is not null) {
                            await OnWorkerEvent(envelope.MachineId, nodeEvent.WorkerEvent);
                        }

                        return await RequestHandling.Ok(req, Enumerable.Empty<BaseResponse>());
                    }
                default:
                    return await _context.RequestHandling.NotOk(req, new Error(ErrorCode.INVALID_REQUEST, new string[] { "Event not of expected type" }), "");
            }
        } catch (InvalidDataException ex) {
            return await _context.RequestHandling.NotOk(req, new Error(ErrorCode.INVALID_REQUEST, new string[] { ex.Message }), "");
        }
    }

    private async Async.Task OnStateUpdate(Guid machineId, NodeStateUpdate ev) {
        var node = await _context.NodeOperations.GetByMachineId(machineId);
        if (node is null) {

        }

    }

    private Async.Task OnWorkerEvent(Guid machineId, WorkerEvent ev) {
        if (ev.Done is not null) {
            return OnWorkerEventDone(machineId, ev.Done);
        }

        if (ev.Running is not null) {
            return OnWorkerEventRunning(machineId, ev.Running);
        }

        throw new InvalidDataException("WorkerEvent should have either Done or Running set");
    }

    private async Async.Task OnWorkerEventRunning(Guid machineId, WorkerRunningEvent ev) {
        var (task, node) = await (
            _context.TaskOperations.GetByTaskId(ev.TaskId),
            _context.NodeOperations.GetByMachineId(machineId));

        if (!node.State.ReadyForReset()) {
            await _context.NodeOperations.SetState(node, NodeState.Busy);
        }

        if (task.State.ShuttingDown()) {
            _log.Info($"ignoring task start from node. machine_id:{machineId} job_id:{task.JobId} task_id:{task.TaskId} (state: {task.State})");
            return;
        }

        _log.Info($"task started on node. machine_id:{machineId} job_id:{task.JobId} task_id:{task.TaskId}");
        await _context.TaskOperations.SetState(task, TaskState.Running);
    }

    private async Async.Task OnWorkerEventDone(Guid machineId, WorkerDoneEvent done) {
        var (task, node) = await (
            _context.TaskOperations.GetByTaskId(done.TaskId),
            _context.NodeOperations.GetByMachineId(machineId));

        // trim stdout/stderr if too long
        done = done with {
            Stderr = LimitText(done.Stderr),
            Stdout = LimitText(done.Stdout),
        };

        if (done.ExitStatus.Success) {
            _log.Info($"task done. {task.JobId}:{task.TaskId} status:{done.ExitStatus}");
            await _context.TaskOperations.MarkStopping(task);

            // keep node if keep-on-completion is set
            if (task.Config.Debug?.Contains(TaskDebugFlag.KeepNodeOnCompletion) == true) {
                node = node with { DebugKeepNode = true };
                await _context.NodeOperations.Replace(node);
            }
        } else {
            await _context.TaskOperations.MarkFailed(
                task,
                new Error(ErrorCode.TASK_FAILED) {
                    Errors = new string[] {
                        $"task failed. exit_status:{done.ExitStatus}",
                        done.Stdout,
                        done.Stderr,
                    },
                });

            // keep node if any keep options are set
            if ((task.Config.Debug?.Contains(TaskDebugFlag.KeepNodeOnFailure) == true)
                || (task.Config.Debug?.Contains(TaskDebugFlag.KeepNodeOnCompletion) == true)) {
                node = node with { DebugKeepNode = true };
                await _context.NodeOperations.Replace(node);
            }
        }

        if (!node.DebugKeepNode) {
            await _context.NodeTasksOperations.Delete(new NodeTasks(machineId, done.TaskId));
        }

        var taskEvent = new TaskEvent(done.TaskId, machineId, new WorkerEvent { Done = done });
        // TODO: save taskEvent
    }

    private static string LimitText(string str) {
        const int MAX_OUTPUT_SIZE = 4096;

        if (str.Length <= MAX_OUTPUT_SIZE) {
            return str;
        }

        return str[..MAX_OUTPUT_SIZE];
    }
}
