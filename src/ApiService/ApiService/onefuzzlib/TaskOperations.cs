using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface ITaskOperations : IStatefulOrm<Task, TaskState> {
    Async.Task<Task?> GetByTaskId(Guid taskId);

    Async.Task<Task?> GetByJobIdAndTaskId(Guid jobId, Guid taskId);


    IAsyncEnumerable<Task> SearchStates(Guid? jobId = null, IEnumerable<TaskState>? states = null);

    IEnumerable<string>? GetInputContainerQueues(TaskConfig config);

    IAsyncEnumerable<Task> SearchExpired();
    Async.Task MarkStopping(Task task);
    Async.Task<TaskVm?> GetReproVmConfig(Task task);
    Async.Task<bool> CheckPrereqTasks(Task task);
    System.Threading.Tasks.Task<Pool?> GetPool(Task task);
    System.Threading.Tasks.Task<Task> SetState(Task task, TaskState state);
}

public class TaskOperations : StatefulOrm<Task, TaskState>, ITaskOperations {
    private readonly IEvents _events;
    private readonly IJobOperations _jobOperations;
    private readonly IPoolOperations _poolOperations;
    private readonly IScalesetOperations _scalesetOperations;

    public TaskOperations(IStorage storage, ILogTracer log, IServiceConfig config, IPoolOperations poolOperations, IScalesetOperations scalesetOperations, IEvents events, IJobOperations jobOperations)
        : base(storage, log, config) {
        _poolOperations = poolOperations;
        _scalesetOperations = scalesetOperations;
        _events = events;
        _jobOperations = jobOperations;
    }

    public async Async.Task<Task?> GetByTaskId(Guid taskId) {
        var data = QueryAsync(filter: $"RowKey eq '{taskId}'");

        return await data.FirstOrDefaultAsync();
    }

    public async Async.Task<Task?> GetByJobIdAndTaskId(Guid jobId, Guid taskId) {
        var data = QueryAsync(filter: $"PartitionKey eq '{jobId}' and RowKey eq '{taskId}'");

        return await data.FirstOrDefaultAsync();
    }
    public IAsyncEnumerable<Task> SearchStates(Guid? jobId = null, IEnumerable<TaskState>? states = null) {
        var queryString = String.Empty;
        if (jobId != null) {
            queryString += $"PartitionKey eq '{jobId}'";
        }

        if (states != null) {
            if (jobId != null) {
                queryString += " and ";
            }

            queryString += "(" + string.Join(
                " or ",
                states.Select(s => $"state eq '{s}'")
            ) + ")";
        }

        return QueryAsync(filter: queryString);
    }

    public IEnumerable<string>? GetInputContainerQueues(TaskConfig config) {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<Task> SearchExpired() {
        var timeFilter = $"end_time lt Datetime'{DateTimeOffset.UtcNow.ToString("o") }'";
        return QueryAsync(filter: timeFilter);
    }

    public async Async.Task MarkStopping(Task task) {
        if (TaskStateHelper.ShuttingDown().Contains(task.State)) {
            _logTracer.Verbose($"ignoring post - task stop calls to stop {task.JobId}:{task.TaskId}");
            return;
        }

        if (TaskStateHelper.HasStarted().Contains(task.State)) {
            await MarkFailed(task, new Error(Code: ErrorCode.TASK_FAILED, Errors: new[] { "task never started" }));

        }
    }

    public async Async.Task MarkFailed(Task task, Error error, List<Task>? taskInJob = null) {
        if (TaskStateHelper.ShuttingDown().Contains(task.State)) {
            _logTracer.Verbose(
                $"ignoring post-task stop failures for {task.JobId}:{task.TaskId}"
            );
            return;
        }

        if (task.Error != null) {
            _logTracer.Verbose(
                $"ignoring additional task error {task.JobId}:{task.TaskId}"
            );
            return;
        }

        _logTracer.Error($"task failed {task.JobId}:{task.TaskId} - {error}");

        task = await SetState(task with { Error = error }, TaskState.Stopping);
        //self.set_state(TaskState.stopping)
        await MarkDependantsFailed(task, taskInJob);
    }

    private async Async.Task MarkDependantsFailed(Task task, List<Task>? taskInJob = null) {
        taskInJob = taskInJob ?? await QueryAsync(filter: $"job_id eq ''{task.JobId}").ToListAsync();

        foreach (var t in taskInJob) {
            if (t.Config.PrereqTasks != null) {
                if (t.Config.PrereqTasks.Contains(t.TaskId)) {
                    await MarkFailed(task, new Error(ErrorCode.TASK_FAILED, new[] { $"prerequisite task failed.  task_id:{t.TaskId}" }), taskInJob);
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
        }

        await this.Replace(task);

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

    private async Async.Task<Task> OnStart(Task task) {
        if (task.EndTime == null) {
            task = task with { EndTime = DateTimeOffset.UtcNow + TimeSpan.FromHours(task.Config.Task.Duration) };

            Job? job = await _jobOperations.Get(task.JobId);
            if (job != null) {
                await _jobOperations.OnStart(job);
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

        var pool = await _poolOperations.GetByName(task.Config.Pool.PoolName);

        if (!pool.IsOk) {
            _logTracer.Info($"unable to find pool from task: {task.TaskId}");
            return null;
        }

        var scaleset = await _scalesetOperations.SearchByPool(task.Config.Pool.PoolName).FirstOrDefaultAsync();

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
                    await MarkFailed(task, new Error(ErrorCode.INVALID_REQUEST, Errors: new[] { "unable to find task" }));
                    return false;
                }

                if (!TaskStateHelper.HasStarted().Contains(t.State)) {
                    return false;
                }
            }
        }
        return true;
    }

    public async System.Threading.Tasks.Task<Pool?> GetPool(Task task) {
        if (task.Config.Pool != null) {
            var pool = await _poolOperations.GetByName(task.Config.Pool.PoolName);
            if (!pool.IsOk) {
                _logTracer.Info(
                    $"unable to schedule task to pool: {task.TaskId} - {pool.ErrorV}"
                );
                return null;
            }
            return pool.OkV;
        } else if (task.Config.Vm != null) {
            var scalesets = _scalesetOperations.Search().Where(s => s.VmSku == task.Config.Vm.Sku && s.Image == task.Config.Vm.Image);

            await foreach (var scaleset in scalesets) {
                if (task.Config.Pool == null) {
                    continue;
                }
                var pool = await _poolOperations.GetByName(task.Config.Pool.PoolName);
                if (!pool.IsOk) {
                    _logTracer.Info(
                        $"unable to schedule task to pool: {task.TaskId} - {pool.ErrorV}"
                    );
                    return null;
                }
                return pool.OkV;
            }
        }

        _logTracer.Warning($"unable to find a scaleset that matches the task prereqs: {task.TaskId}");
        return null;

    }
}
