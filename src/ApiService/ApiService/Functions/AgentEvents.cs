using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service.Functions;

public class AgentEvents {
    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;

    public AgentEvents(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    [Function("AgentEvents")]
    public Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "POST", Route="agents/events")]
        HttpRequestData req)
        => _auth.CallIfAgent(req, Post);

    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<NodeStateEnvelope>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, context: "node event");
        }

        var envelope = request.OkV;
        _log.WithTag("HttpRequest", "POST").Info($"node event: {envelope.MachineId:Tag:MachineId} {EntityConverter.ToJsonString(envelope):Tag:Event}");

        var error = envelope.Event switch {
            NodeStateUpdate updateEvent => await OnStateUpdate(envelope.MachineId, updateEvent),
            WorkerEvent workerEvent => await OnWorkerEvent(envelope.MachineId, workerEvent),
            NodeEvent nodeEvent => await OnNodeEvent(envelope.MachineId, nodeEvent),
            _ => new Error(ErrorCode.INVALID_REQUEST, new string[] { $"invalid node event: {envelope.Event.GetType().Name}" }),
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
            _log.Warning($"unable to process state update event. {machineId:Tag:MachineId} {ev:Tag:Event}");
            return null;
        }

        if (ev.State == NodeState.Free) {
            if (node.ReimageRequested || node.DeleteRequested) {
                _log.Info($"stopping free node with reset flags: {machineId:Tag:MachineId}");
                // discard result: node not used after this point
                _ = await _context.NodeOperations.Stop(node);
                return null;
            }

            if (await _context.NodeOperations.CouldShrinkScaleset(node)) {
                _log.Info($"stopping free node to resize scaleset: {machineId:Tag:MachineId}");
                // discard result: node not used after this point
                _ = await _context.NodeOperations.SetHalt(node);
                return null;
            }
        }

        if (ev.State == NodeState.Init) {
            if (node.DeleteRequested) {
                _log.Info($"stopping node (init and delete_requested): {machineId:Tag:MachineId}");
                // discard result: node not used after this point
                _ = await _context.NodeOperations.Stop(node);
                return null;
            }

            // Don’t check reimage_requested, as nodes only send 'init' state once.  If
            // they send 'init' with reimage_requested, it's because the node was reimaged
            // successfully.
            // Node has to be in state other than Init, otherwise SetState ignores the change, and therefore
            // InitializedAt field is never set
            node = node with { ReimageRequested = false, InitializedAt = DateTimeOffset.UtcNow };
            // discard result: node not used after this point
            _ = await _context.NodeOperations.SetState(node, ev.State);
            return null;
        }

        node = await _context.NodeOperations.SetState(node, ev.State);

        if (ev.State == NodeState.Free) {
            _log.Info($"node now available for work: {machineId:Tag:MachineId}");
        } else if (ev.State == NodeState.SettingUp) {
            if (ev.Data is NodeSettingUpEventData settingUpData) {
                if (!settingUpData.Tasks.Any()) {
                    return new Error(ErrorCode.INVALID_REQUEST, Errors: new string[] {
                        $"setup without tasks.  machine_id: {machineId}",
                    });
                }

                foreach (var taskId in settingUpData.Tasks) {
                    var task = await _context.TaskOperations.GetByTaskId(taskId);
                    if (task is null) {
                        return new Error(
                            ErrorCode.INVALID_REQUEST,
                            Errors: new string[] { $"unable to find task: {taskId}" });
                    }

                    _log.Info($"node starting task. {machineId:Tag:MachineId} {task.JobId:Tag:JobId} {task.TaskId:Tag:TaskId}");

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
                        State: NodeTaskState.SettingUp);
                    var r = await _context.NodeTasksOperations.Replace(nodeTask);
                    if (!r.IsOk) {
                        _log.WithHttpStatus(r.ErrorV).Error($"Failed to replace node task {task.TaskId:Tag:TaskId}");
                    }
                }
            }
        } else if (ev.State == NodeState.Done) {
            Error? error = null;
            if (ev.Data is NodeDoneEventData doneData) {
                if (doneData.Error is not null) {
                    var errorText = EntityConverter.ToJsonString(doneData);
                    error = new Error(ErrorCode.TASK_FAILED, Errors: new string[] { errorText });
                    _log.Error($"node 'done' {machineId:Tag:MachineId} - {errorText:Tag:Error}");
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

        return new Error(
            Code: ErrorCode.INVALID_REQUEST,
            Errors: new string[] { "WorkerEvent should have either 'done' or 'running' set" });
    }

    private async Async.Task<Error?> OnWorkerEventRunning(Guid machineId, WorkerRunningEvent running) {
        var (task, node) = await (
            _context.TaskOperations.GetByTaskId(running.TaskId),
            _context.NodeOperations.GetByMachineId(machineId));

        if (task is null) {
            return new Error(
                Code: ErrorCode.INVALID_REQUEST,
                Errors: new string[] { $"unable to find task: {running.TaskId}" });
        }

        if (node is null) {
            return new Error(
                Code: ErrorCode.INVALID_REQUEST,
                Errors: new string[] { $"unable to find node: {machineId}" });
        }

        if (!node.State.ReadyForReset()) {
            // discard result: node not used after this point
            _ = await _context.NodeOperations.SetState(node, NodeState.Busy);
        }

        var nodeTask = new NodeTasks(
            MachineId: machineId,
            TaskId: running.TaskId,
            State: NodeTaskState.Running);
        var r = await _context.NodeTasksOperations.Replace(nodeTask);
        if (!r.IsOk) {
            _log.WithHttpStatus(r.ErrorV).Error($"failed to replace node task {nodeTask.TaskId:Tag:TaskId}");
        }

        if (task.State.ShuttingDown()) {
            _log.Info($"ignoring task start from node. {machineId:Tag:MachineId} {task.JobId:Tag:JobId} {task.TaskId:Tag:TaskId} ({task.State:Tag:State})");
            return null;
        }

        _log.Info($"task started on node. {machineId:Tag:MachineId} {task.JobId:Tag:JobId} {task.TaskId:Tag:TaskId}");
        task = await _context.TaskOperations.SetState(task, TaskState.Running);

        var taskEvent = new TaskEvent(
            TaskId: task.TaskId,
            MachineId: machineId,
            EventData: new WorkerEvent(Running: running));
        r = await _context.TaskEventOperations.Replace(taskEvent);
        if (!r.IsOk) {
            _log.WithHttpStatus(r.ErrorV).Error($"failed to replace taskEvent {taskEvent.TaskId:Tag:TaskId}");
        }

        return null;
    }

    private async Async.Task<Error?> OnWorkerEventDone(Guid machineId, WorkerDoneEvent done) {
        var (task, node) = await (
            _context.TaskOperations.GetByTaskId(done.TaskId),
            _context.NodeOperations.GetByMachineId(machineId));

        if (task is null) {
            return new Error(
                Code: ErrorCode.INVALID_REQUEST,
                Errors: new string[] { $"unable to find task: {done.TaskId}" });
        }

        if (node is null) {
            return new Error(
                Code: ErrorCode.INVALID_REQUEST,
                Errors: new string[] { $"unable to find node: {machineId}" });
        }

        // trim stdout/stderr if too long
        done = done with {
            Stderr = LimitText(done.Stderr),
            Stdout = LimitText(done.Stdout),
        };

        if (done.ExitStatus.Success) {
            _log.Info($"task done. {task.JobId:Tag:JobId}:{task.TaskId:Tag:TaskId} {done.ExitStatus:Tag:Status}");
            await _context.TaskOperations.MarkStopping(task);

            // keep node if keep-on-completion is set
            if (task.Config.Debug?.Contains(TaskDebugFlag.KeepNodeOnCompletion) == true) {
                node = node with { DebugKeepNode = true };
                var r = await _context.NodeOperations.Replace(node);
                if (!r.IsOk) {
                    _log.WithHttpStatus(r.ErrorV).Error($"keepNodeOnCompletion: failed to replace node {node.MachineId:Tag:MachineId} when setting debug keep node to true");
                }
            }
        } else {
            await _context.TaskOperations.MarkFailed(
                task,
                new Error(
                    Code: ErrorCode.TASK_FAILED,
                    Errors: new string[] {
                        $"task failed. exit_status:{done.ExitStatus}",
                        done.Stdout,
                        done.Stderr,
                    }));

            // keep node if any keep options are set
            if ((task.Config.Debug?.Contains(TaskDebugFlag.KeepNodeOnFailure) == true)
                || (task.Config.Debug?.Contains(TaskDebugFlag.KeepNodeOnCompletion) == true)) {
                node = node with { DebugKeepNode = true };
                var r = await _context.NodeOperations.Replace(node);
                if (!r.IsOk) {
                    _log.WithHttpStatus(r.ErrorV).Error($"keepNodeOnfFailure: failed to replace node {node.MachineId:Tag:MachineId} when setting debug keep node to true");
                }
            }
        }

        if (!node.DebugKeepNode) {
            var r = await _context.NodeTasksOperations.Delete(new NodeTasks(machineId, done.TaskId));
            if (!r.IsOk) {
                _log.WithHttpStatus(r.ErrorV).Error($"failed to deleting node task {done.TaskId:Tag:TaskId} for: {machineId:Tag:MachineId} since DebugKeepNode is false");
            }
        }

        var taskEvent = new TaskEvent(done.TaskId, machineId, new WorkerEvent { Done = done });
        var r1 = await _context.TaskEventOperations.Replace(taskEvent);
        if (!r1.IsOk) {
            _log.WithHttpStatus(r1.ErrorV).Error($"failed to update task event for done task {done.TaskId:Tag:TaskId}");
        }
        return null;
    }

    private static string LimitText(string str) {
        const int MAX_OUTPUT_SIZE = 4096;

        if (str.Length <= MAX_OUTPUT_SIZE) {
            return str;
        }

        return str[..MAX_OUTPUT_SIZE];
    }
}
