using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface ITaskOperations : IStatefulOrm<Task, TaskState> {
    Async.Task<Task?> GetByTaskId(Guid taskId);

    IAsyncEnumerable<Task> GetByTaskIds(IEnumerable<Guid> taskId);

    IAsyncEnumerable<Task> GetByJobId(Guid jobId);

    Async.Task<Task?> GetByJobIdAndTaskId(Guid jobId, Guid taskId);


    IAsyncEnumerable<Task> SearchStates(Guid? jobId = null, IEnumerable<TaskState>? states = null);

    Result<IEnumerable<Container>?, TaskConfigError> GetInputContainerQueues(TaskConfig config);

    IAsyncEnumerable<Task> SearchExpired();
    Async.Task MarkStopping(Task task);
    Async.Task MarkFailed(Task task, Error error, List<Task>? taskInJob = null);

    Async.Task<TaskVm?> GetReproVmConfig(Task task);
    Async.Task<bool> CheckPrereqTasks(Task task);
    Async.Task<Pool?> GetPool(Task task);
    Async.Task<Task> SetState(Task task, TaskState state);
    Async.Task<OneFuzzResult<Task>> Create(TaskConfig config, Guid jobId, UserInfo userInfo);

    // state transitions:
    Async.Task<Task> Init(Task task);
    Async.Task<Task> Waiting(Task task);
    Async.Task<Task> Scheduled(Task task);
    Async.Task<Task> SettingUp(Task task);
    Async.Task<Task> Running(Task task);
    Async.Task<Task> Stopping(Task task);
    Async.Task<Task> Stopped(Task task);
    Async.Task<Task> WaitJob(Task task);
}

public class TaskOperations : StatefulOrm<Task, TaskState, TaskOperations>, ITaskOperations {


    public TaskOperations(ILogTracer log, IOnefuzzContext context)
        : base(log, context) {

    }

    public async Async.Task<Task?> GetByTaskId(Guid taskId) {
        return await GetByTaskIds(new[] { taskId }).FirstOrDefaultAsync();
    }

    public IAsyncEnumerable<Task> GetByTaskIds(IEnumerable<Guid> taskId) {
        return QueryAsync(filter: Query.RowKeys(taskId.Select(t => t.ToString())));
    }

    public IAsyncEnumerable<Task> GetByJobId(Guid jobId) {
        return QueryAsync(Query.PartitionKey(jobId.ToString()));
    }

    public async Async.Task<Task?> GetByJobIdAndTaskId(Guid jobId, Guid taskId) {
        var data = QueryAsync(Query.SingleEntity(jobId.ToString(), taskId.ToString()));
        return await data.FirstOrDefaultAsync();
    }
    public IAsyncEnumerable<Task> SearchStates(Guid? jobId = null, IEnumerable<TaskState>? states = null) {
        if (states is not null && !states.Any()) {
            states = null;
        }

        var queryString =
            (jobId, states) switch {
                (null, null) => "",
                (Guid id, null) => Query.PartitionKey(id.ToString()),
                (null, IEnumerable<TaskState> s) => Query.EqualAnyEnum("state", s),
                (Guid id, IEnumerable<TaskState> s) => Query.And(Query.PartitionKey(id.ToString()), Query.EqualAnyEnum("state", s)),
            };

        return QueryAsync(filter: queryString);
    }

    public Result<IEnumerable<Container>?, TaskConfigError> GetInputContainerQueues(TaskConfig config) {

        if (!Defs.TASK_DEFINITIONS.ContainsKey(config.Task.Type)) {
            return Result<IEnumerable<Container>?, TaskConfigError>.Error(new TaskConfigError($"unsupported task type: {config.Task.Type}"));
        }

        var containerType = Defs.TASK_DEFINITIONS[config.Task.Type].MonitorQueue;
        if (containerType is not null && config.Containers is not null)
            return Result<IEnumerable<Container>?, TaskConfigError>.Ok(config.Containers.Where(x => x.Type == containerType).Select(x => x.Name));
        else
            return Result<IEnumerable<Container>?, TaskConfigError>.Ok(null);
    }

    public IAsyncEnumerable<Task> SearchExpired() {
        var timeFilter = Query.OlderThan("end_time", DateTimeOffset.UtcNow);
        var stateFilter = Query.EqualAnyEnum("state", TaskStateHelper.AvailableStates);
        var filter = Query.And(stateFilter, timeFilter);
        return QueryAsync(filter: filter);
    }

    public async Async.Task MarkStopping(Task task) {
        if (task.State.ShuttingDown()) {
            _logTracer.Verbose($"ignoring post - task stop calls to stop {task.JobId}:{task.TaskId}");
            return;
        }

        if (!task.State.HasStarted()) {
            await MarkFailed(task, new Error(Code: ErrorCode.TASK_FAILED, Errors: new[] { "task never started" }));
        } else {
            var _ = await SetState(task, TaskState.Stopping);
        }
    }

    public async Async.Task MarkFailed(Task task, Error error, List<Task>? taskInJob = null) {
        if (task.State.ShuttingDown()) {
            return;
        }

        if (task.Error != null) {
            return;
        }

        _logTracer.Info($"task failed {task.JobId}:{task.TaskId} - {error}");

        task = await SetState(task with { Error = error }, TaskState.Stopping);
        await MarkDependantsFailed(task, taskInJob);
    }

    private async Async.Task MarkDependantsFailed(Task task, List<Task>? taskInJob = null) {
        taskInJob ??= await SearchByPartitionKeys(new[] { $"{task.JobId}" }).ToListAsync();

        foreach (var t in taskInJob) {
            if (t.Config.PrereqTasks != null) {
                if (t.Config.PrereqTasks.Contains(task.TaskId)) {
                    await MarkFailed(t, new Error(ErrorCode.TASK_FAILED, new[] { $"prerequisite task failed.  task_id:{t.TaskId}" }), taskInJob);
                }
            }
        }
    }

    public async Async.Task<Task> SetState(Task task, TaskState state) {
        if (task.State == state) {
            return task;
        }

        if (task.State == TaskState.Running || task.State == TaskState.SettingUp) {
            task = await OnStart(task with { State = state });
        } else {
            task = task with { State = state };
        }

        var r = await Replace(task);
        if (!r.IsOk) {
            _logTracer.WithHttpStatus(r.ErrorV).Error($"Failed to replace task with jobid: {task.JobId} and taskid: {task.TaskId}");
        }

        var _events = _context.Events;
        if (task.State == TaskState.Stopped) {
            if (task.Error != null) {
                await _events.SendEvent(new EventTaskFailed(
                    JobId: task.JobId,
                    TaskId: task.TaskId,
                    Error: task.Error,
                    UserInfo: task.UserInfo,
                    Config: task.Config)
                    );
            } else {
                await _events.SendEvent(new EventTaskStopped(
                   JobId: task.JobId,
                   TaskId: task.TaskId,
                   UserInfo: task.UserInfo,
                   Config: task.Config)
                   );
            }
        } else {
            await _events.SendEvent(new EventTaskStateUpdated(
                   JobId: task.JobId,
                   TaskId: task.TaskId,
                   State: task.State,
                   EndTime: task.EndTime,
                   Config: task.Config)
                   );
        }

        return task;
    }

    public async Task<OneFuzzResult<Task>> Create(TaskConfig config, Guid jobId, UserInfo userInfo) {

        Os os;
        if (config.Vm != null) {
            var osResult = await _context.ImageOperations.GetOs(config.Vm.Region, config.Vm.Image);
            if (!osResult.IsOk) {
                return OneFuzzResult<Task>.Error(osResult.ErrorV);
            }
            os = osResult.OkV;
        } else if (config.Pool != null) {
            var pool = await _context.PoolOperations.GetByName(config.Pool.PoolName);

            if (!pool.IsOk) {
                return OneFuzzResult<Task>.Error(pool.ErrorV);
            }
            os = pool.OkV.Os;
        } else {
            return OneFuzzResult<Task>.Error(ErrorCode.INVALID_CONFIGURATION, "task must have vm or pool");
        }

        var task = new Task(jobId, Guid.NewGuid(), TaskState.Init, os, config, UserInfo: userInfo);

        var r = await _context.TaskOperations.Insert(task);
        if (!r.IsOk) {
            _logTracer.WithHttpStatus(r.ErrorV).Error($"failed to insert task {task.TaskId}");
        }
        await _context.Events.SendEvent(new EventTaskCreated(jobId, task.TaskId, config, userInfo));

        _logTracer.Info($"created task. job_id:{jobId} task_id:{task.TaskId} type:{task.Config.Task.Type}");
        return OneFuzzResult<Task>.Ok(task);
    }

    private async Async.Task<Task> OnStart(Task task) {
        if (task.EndTime == null) {
            task = task with { EndTime = DateTimeOffset.UtcNow + TimeSpan.FromHours(task.Config.Task.Duration) };

            var jobOperations = _context.JobOperations;
            Job? job = await jobOperations.Get(task.JobId);
            if (job != null) {
                await jobOperations.OnStart(job);
            }
        }
        return task;

    }

    public async Async.Task<TaskVm?> GetReproVmConfig(Task task) {
        if (task.Config.Vm != null) {
            return task.Config.Vm;
        }

        if (task.Config.Pool == null) {
            throw new Exception($"either pool or vm must be specified: {task.TaskId}");
        }

        var pool = await _context.PoolOperations.GetByName(task.Config.Pool.PoolName);

        if (!pool.IsOk) {
            _logTracer.Info($"unable to find pool from task: {task.TaskId}");
            return null;
        }

        var scaleset = await _context.ScalesetOperations.SearchByPool(task.Config.Pool.PoolName).FirstOrDefaultAsync();

        if (scaleset == null) {
            _logTracer.Warning($"no scalesets are defined for task: {task.JobId}:{task.TaskId}");
            return null;
        }

        return new TaskVm(scaleset.Region, scaleset.VmSku, scaleset.Image, null);
    }

    public async Async.Task<bool> CheckPrereqTasks(Task task) {
        if (task.Config.PrereqTasks != null) {
            foreach (var taskId in task.Config.PrereqTasks) {
                var t = await GetByTaskId(taskId);

                // if a prereq task fails, then mark this task as failed
                if (t == null) {
                    await MarkFailed(task, new Error(ErrorCode.INVALID_REQUEST, Errors: new[] { "unable to find prereq task" }));
                    return false;
                }

                if (t.JobId != task.JobId) {
                    _logTracer.Critical("Tasks are not from the same job");
                }

                if (!t.State.HasStarted()) {
                    return false;
                }
            }
        }
        return true;
    }

    public async Async.Task<Pool?> GetPool(Task task) {
        // Note: if the behaviour of this method changes,
        // then Scheduler.GetPoolKey will probably also need to change

        if (task.Config.Pool is TaskPool p) {
            var pool = await _context.PoolOperations.GetByName(p.PoolName);
            if (!pool.IsOk) {
                _logTracer.Info(
                    $"unable to schedule task to pool [{task.Config.Pool.PoolName}]: {task.TaskId} - {pool.ErrorV}"
                );
                return null;
            }

            return pool.OkV;
        }

        if (task.Config.Vm is TaskVm taskVm) {
            var scalesets = _context.ScalesetOperations.Search().Where(s => s.VmSku == taskVm.Sku && s.Image == taskVm.Image);
            await foreach (var scaleset in scalesets) {
                var pool = await _context.PoolOperations.GetByName(scaleset.PoolName);
                if (!pool.IsOk) {
                    _logTracer.Info(
                        $"unable to schedule task to pool [{scaleset.PoolName}]: {task.TaskId} - {pool.ErrorV}"
                    );
                    return null;
                }

                return pool.OkV;
            }
        }

        _logTracer.Warning($"unable to find a scaleset that matches the task prereqs: {task.TaskId}");
        return null;
    }

    public async Async.Task<Task> Init(Task task) {
        await _context.Queue.CreateQueue($"{task.TaskId}", StorageType.Corpus);
        return await SetState(task, TaskState.Waiting);
    }


    public async Async.Task<Task> Stopping(Task task) {
        _logTracer.Info($"stopping task : {task.JobId}, {task.TaskId}");
        await _context.NodeOperations.StopTask(task.TaskId);
        var anyRemainingNodes = await _context.NodeTasksOperations.GetNodesByTaskId(task.TaskId).AnyAsync();
        if (!anyRemainingNodes) {
            return await Stopped(task);
        }
        return task;
    }

    public async Async.Task<Task> Stopped(Task task) {
        task = await SetState(task, TaskState.Stopped);
        await _context.Queue.DeleteQueue($"{task.TaskId}", StorageType.Corpus);

        //     # TODO: we need to 'unschedule' this task from the existing pools
        var job = await _context.JobOperations.Get(task.JobId);
        if (job != null) {
            await _context.JobOperations.StopIfAllDone(job);
        }

        return task;
    }

    public Task<Task> Waiting(Task task) {
        // nothing to do
        return Async.Task.FromResult(task);
    }

    public Task<Task> Scheduled(Task task) {
        // nothing to do
        return Async.Task.FromResult(task);
    }

    public Task<Task> SettingUp(Task task) {
        // nothing to do
        return Async.Task.FromResult(task);
    }

    public Task<Task> Running(Task task) {
        // nothing to do
        return Async.Task.FromResult(task);
    }

    public Task<Task> WaitJob(Task task) {
        // nothing to do
        return Async.Task.FromResult(task);
    }
}
