using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.Auth;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
namespace Microsoft.OneFuzz.Service.Functions;

public class AgentEvents {
    private readonly ILogger _log;
    private readonly IOnefuzzContext _context;

    public AgentEvents(ILogger<AgentEvents> log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("AgentEvents")]
    [Authorize(Allow.Agent)]
    public async Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "POST", Route="agents/events")]
        HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<NodeStateEnvelope>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, context: "node event");
        }

        var envelope = request.OkV;
        _log.AddTag("HttpRequest", "POST");
        _log.LogInformation("node event: {MachineId} {Event}", envelope.MachineId, EntityConverter.ToJsonString(envelope));

        var error = envelope.Event switch {
            NodeStateUpdate updateEvent => await OnStateUpdate(envelope.MachineId, updateEvent),
            WorkerEvent workerEvent => await OnWorkerEvent(envelope.MachineId, workerEvent),
            NodeEvent nodeEvent => await OnNodeEvent(envelope.MachineId, nodeEvent),
            _ => Error.Create(ErrorCode.INVALID_REQUEST, $"invalid node event: {envelope.Event.GetType().Name}"),
        };

        if (error is Error e) {
            return await _context.RequestHandling.NotOk(req, e, context: "node event");
        } else {
            return await RequestHandling.Ok(req, new BoolResult(true));
        }
    }

    private async Async.Task<Error?> OnNodeEvent(Guid machineId, NodeEvent nodeEvent) {
        if (nodeEvent.StateUpdate is not null) {
            var result = await OnStateUpdate(machineId, nodeEvent.StateUpdate);
            if (result is not null) {
                return result;
            }
        }

        if (nodeEvent.WorkerEvent is not null) {
            var result = await OnWorkerEvent(machineId, nodeEvent.WorkerEvent);
            if (result is not null) {
                return result;
            }
        }

        return null;
    }

    private async Async.Task<Error?> OnStateUpdate(Guid machineId, NodeStateUpdate ev) {
        var node = await _context.NodeOperations.GetByMachineId(machineId);
        if (node is null) {
            _log.LogWarning("unable to process state update event. {MachineId} {Event}", machineId, ev);
            return null;
        }

        if (ev.State == NodeState.Free) {
            if (node.ReimageRequested || node.DeleteRequested) {
                if (!node.Managed) {
                    return null;
                }
                _log.LogInformation("stopping free node with reset flags: {MachineId}", machineId);
                // discard result: node not used after this point
                _ = await _context.NodeOperations.Stop(node);
                return null;
            }

            if (await _context.NodeOperations.CouldShrinkScaleset(node)) {
                _log.LogInformation("stopping free node to resize scaleset: {MachineId}", machineId);
                // discard result: node not used after this point
                _ = await _context.NodeOperations.SetHalt(node);
                return null;
            }
        }

        if (ev.State == NodeState.Init) {
            if (node.DeleteRequested) {
                _log.LogInformation("stopping node (init and delete_requested): {MachineId}", machineId);
                // discard result: node not used after this point
                _ = await _context.NodeOperations.Stop(node);
                return null;
            }

            // Don’t check reimage_requested, as nodes only send 'init' state once.  If
            // they send 'init' with reimage_requested, it's because the node was reimaged
            // successfully.
            node = node with { ReimageRequested = false, InitializedAt = DateTimeOffset.UtcNow };
            // discard result: node not used after this point
            _ = await _context.NodeOperations.SetState(node, ev.State);
            return null;
        }

        node = await _context.NodeOperations.SetState(node, ev.State);

        if (ev.State == NodeState.Free) {
            _log.LogInformation("node now available for work: {MachineId}", machineId);
        } else if (ev.State == NodeState.SettingUp) {
            if (ev.Data is NodeSettingUpEventData settingUpData) {
                if (!settingUpData.Tasks.Any()) {
                    return Error.Create(ErrorCode.INVALID_REQUEST,
                        $"setup without tasks.  machine_id: {machineId}"
                    );
                }

                foreach (var taskData in settingUpData.Tasks) {
                    var task = await _context.TaskOperations.GetByJobIdAndTaskId(taskData.JobId, taskData.TaskId);
                    if (task is null) {
                        return Error.Create(
                            ErrorCode.INVALID_REQUEST,
                            $"unable to find task: {taskData.JobId} {taskData.TaskId}");
                    }

                    _log.LogInformation("node starting task. {MachineId} {JobId} {TaskId}", machineId, task.JobId, task.TaskId);

                    // The task state may be `running` if it has `vm_count` > 1, and
                    // another node is concurrently executing the task. If so, leave
                    // the state as-is, to represent the max progress made.
                    //
                    // Other states we would want to preserve are excluded by the
                    // outermost conditional check.
                    if (task.State != TaskState.Running && task.State != TaskState.SettingUp) {
                        task = await _context.TaskOperations.SetState(task, TaskState.SettingUp);
                    }

                    var nodeTask = new NodeTasks(
                        MachineId: machineId,
                        TaskId: task.TaskId,
                        JobId: task.JobId,
                        State: NodeTaskState.SettingUp);
                    var r = await _context.NodeTasksOperations.Replace(nodeTask);
                    if (!r.IsOk) {
                        _log.AddHttpStatus(r.ErrorV);
                        _log.LogError("Failed to replace node task {TaskId}", task.TaskId);
                    }
                }
            }
        } else if (ev.State == NodeState.Done) {
            Error? error = null;
            if (ev.Data is NodeDoneEventData doneData) {
                if (doneData.Error is not null) {
                    var errorText = EntityConverter.ToJsonString(doneData);
                    error = Error.Create(ErrorCode.TASK_FAILED, errorText);
                    _log.LogError("node 'done' {MachineId} - {Error}", machineId, errorText);
                }
            }

            // if tasks are running on the node when it reports as Done
            // those are stopped early
            await _context.NodeOperations.MarkTasksStoppedEarly(node, error);
            // discard result: node not used after this point
            _ = await _context.NodeOperations.ToReimage(node, done: true);
        }

        return null;
    }

    private async Async.Task<Error?> OnWorkerEvent(Guid machineId, WorkerEvent ev) {
        if (ev.Done is not null) {
            return await OnWorkerEventDone(machineId, ev.Done);
        }

        if (ev.Running is not null) {
            return await OnWorkerEventRunning(machineId, ev.Running);
        }

        return Error.Create(
            ErrorCode.INVALID_REQUEST,
            "WorkerEvent should have either 'done' or 'running' set");
    }

    private async Async.Task<Error?> OnWorkerEventRunning(Guid machineId, WorkerRunningEvent running) {
        var (task, node) = await (
            (running.JobId.HasValue
                ? _context.TaskOperations.GetByJobIdAndTaskId(running.JobId.Value, running.TaskId)
                // old agent, fallback
                : _context.TaskOperations.GetByTaskIdSlow(running.TaskId)),
            _context.NodeOperations.GetByMachineId(machineId));

        if (task is null) {
            return Error.Create(ErrorCode.INVALID_REQUEST, $"unable to find task: {running.TaskId}");
        }

        if (node is null) {
            return Error.Create(ErrorCode.INVALID_REQUEST, $"unable to find node: {machineId}");
        }

        if (!node.State.ReadyForReset()) {
            // discard result: node not used after this point
            _ = await _context.NodeOperations.SetState(node, NodeState.Busy);
        }

        var nodeTask = new NodeTasks(
            MachineId: machineId,
            TaskId: running.TaskId,
            JobId: running.JobId,
            State: NodeTaskState.Running);
        var r = await _context.NodeTasksOperations.Replace(nodeTask);
        if (!r.IsOk) {
            _log.AddHttpStatus(r.ErrorV);
            _log.LogError("failed to replace node task {TaskId}", nodeTask.TaskId);
        }

        if (task.State.ShuttingDown()) {
            _log.LogInformation("ignoring task start from node. {MachineId} {JobId} {TaskId} ({State})", machineId, task.JobId, task.TaskId, task.State);
            return null;
        }

        _log.LogInformation("task started on node. {MachineId} {JobId} {TaskId}", machineId, task.JobId, task.TaskId);
        task = await _context.TaskOperations.SetState(task, TaskState.Running);

        var taskEvent = new TaskEvent(
            TaskId: task.TaskId,
            MachineId: machineId,
            EventData: new WorkerEvent(Running: running));
        r = await _context.TaskEventOperations.Replace(taskEvent);
        if (!r.IsOk) {
            _log.AddHttpStatus(r.ErrorV);
            _log.LogError("failed to replace taskEvent {TaskId}", taskEvent.TaskId);
        }

        return null;
    }

    private async Async.Task<Error?> OnWorkerEventDone(Guid machineId, WorkerDoneEvent done) {

        var (task, node) = await (
            (done.JobId.HasValue
                ? _context.TaskOperations.GetByJobIdAndTaskId(done.JobId.Value, done.TaskId)
                // old agent, fall back
                : _context.TaskOperations.GetByTaskIdSlow(done.TaskId)),
            _context.NodeOperations.GetByMachineId(machineId));

        if (task is null) {
            return Error.Create(ErrorCode.INVALID_REQUEST, $"unable to find task: {done.TaskId}");
        }

        if (node is null) {
            return Error.Create(ErrorCode.INVALID_REQUEST, $"unable to find node: {machineId}");
        }

        // trim stdout/stderr if too long
        done = done with {
            Stderr = LimitText(done.Stderr),
            Stdout = LimitText(done.Stdout),
        };

        if (done.ExitStatus.Success) {
            _log.LogInformation("task done. {JobId}:{TaskId} {Status}", task.JobId, task.TaskId, done.ExitStatus);
            await _context.TaskOperations.MarkStopping(task, "task is done");

            // keep node if keep-on-completion is set
            if (task.Config.Debug?.Contains(TaskDebugFlag.KeepNodeOnCompletion) == true) {
                node = node with { DebugKeepNode = true };
                var r = await _context.NodeOperations.Replace(node);
                if (!r.IsOk) {
                    _log.AddHttpStatus(r.ErrorV);
                    _log.LogError("keepNodeOnCompletion: failed to replace node {MachineId} when setting debug keep node to true", node.MachineId);
                }
            }
        } else {
            await _context.TaskOperations.MarkFailed(
                task,
                Error.Create(
                    ErrorCode.TASK_FAILED,
                    $"task failed. exit_status:{done.ExitStatus}",
                    done.Stdout,
                    done.Stderr
                    ));

            // keep node if any keep options are set
            if ((task.Config.Debug?.Contains(TaskDebugFlag.KeepNodeOnFailure) == true)
                || (task.Config.Debug?.Contains(TaskDebugFlag.KeepNodeOnCompletion) == true)) {
                node = node with { DebugKeepNode = true };
                var r = await _context.NodeOperations.Replace(node);
                if (!r.IsOk) {
                    _log.AddHttpStatus(r.ErrorV);
                    _log.LogError("keepNodeOnfFailure: failed to replace node {MachineId} when setting debug keep node to true", node.MachineId);
                }
            }
        }

        if (!node.DebugKeepNode) {
            var r = await _context.NodeTasksOperations.Delete(new NodeTasks(machineId, done.TaskId, done.JobId));
            if (!r.IsOk) {
                _log.AddHttpStatus(r.ErrorV);
                _log.LogError("failed to deleting node task {TaskId} for: {MachineId} since DebugKeepNode is false", done.TaskId, machineId);
            }
        }

        var taskEvent = new TaskEvent(done.TaskId, machineId, new WorkerEvent { Done = done });
        var r1 = await _context.TaskEventOperations.Replace(taskEvent);
        if (!r1.IsOk) {
            _log.AddHttpStatus(r1.ErrorV);
            _log.LogError("failed to update task event for done task {TaskId}", done.TaskId);
        }
        return null;
    }

    private static string LimitText(string str) {
        const int MAX_OUTPUT_SIZE = 4096;

        if (str.Length <= MAX_OUTPUT_SIZE) {
            return str;
        }

        return str[(str.Length - MAX_OUTPUT_SIZE)..];
    }
}
