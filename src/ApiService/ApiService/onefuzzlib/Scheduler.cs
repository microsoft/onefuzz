using Azure.Storage.Sas;

namespace Microsoft.OneFuzz.Service;


public interface IScheduler {
    Async.Task ScheduleTasks();
}

public class Scheduler : IScheduler {
    private readonly ITaskOperations _taskOperations;
    private readonly IConfig _config;
    private readonly IPoolOperations _poolOperations;
    private readonly ILogTracer _logTracer;
    private readonly IJobOperations _jobOperations;
    private readonly IContainers _containers;

    // TODO: eventually, this should be tied to the pool.
    const int MAX_TASKS_PER_SET = 10;

    public Scheduler(ITaskOperations taskOperations, IConfig config, IPoolOperations poolOperations, ILogTracer logTracer, IJobOperations jobOperations, IContainers containers) {
        _taskOperations = taskOperations;
        _config = config;
        _poolOperations = poolOperations;
        _logTracer = logTracer;
        _jobOperations = jobOperations;
        _containers = containers;
    }

    public async Async.Task ScheduleTasks() {
        var tasks = await _taskOperations.SearchStates(states: new[] { TaskState.Waiting }).ToDictionaryAsync(x => x.TaskId);
        var seen = new HashSet<Guid>();

        var buckets = BucketTasks(tasks.Values);

        foreach (var bucketedTasks in buckets) {
            foreach (var chunks in bucketedTasks.Chunk(MAX_TASKS_PER_SET)) {
                var result = await BuildWorkSet(chunks);
                if (result == null) {
                    continue;
                }
                var (bucketConfig, workSet) = result.Value;

                if (await ScheduleWorkset(workSet, bucketConfig.pool, bucketConfig.count)) {
                    foreach (var workUnit in workSet.WorkUnits) {
                        var task1 = tasks[workUnit.TaskId];
                        Task task = await _taskOperations.SetState(task1, TaskState.Scheduled);
                        seen.Add(task.TaskId);
                    }
                }
            }
        }

        var notReadyCount = tasks.Count - seen.Count;
        if (notReadyCount > 0) {
            _logTracer.Info($"tasks not ready {notReadyCount}");
        }
    }

    private async Async.Task<bool> ScheduleWorkset(WorkSet workSet, Pool pool, int count) {
        if (!PoolStateHelper.Available().Contains(pool.State)) {
            _logTracer.Info($"pool not available for work: {pool.Name} state: {pool.State}");
            return false;
        }

        for (var i = 0; i < count; i++) {
            if (!await _poolOperations.ScheduleWorkset(pool, workSet)) {
                _logTracer.Error($"unable to schedule workset. pool:{pool.Name} workset: {workSet}");
                return false;
            }
        }
        return true;
    }

    private async Async.Task<(BucketConfig, WorkSet)?> BuildWorkSet(Task[] tasks) {
        var taskIds = tasks.Select(x => x.TaskId).ToHashSet();
        var work_units = new List<WorkUnit>();

        BucketConfig? bucketConfig = null;
        foreach (var task in tasks) {
            if ((task.Config.PrereqTasks?.Count ?? 0) > 0) {
                // if all of the prereqs are in this bucket, they will be
                // scheduled together
                if (!taskIds.IsSupersetOf(task.Config.PrereqTasks!)) {
                    if (!(await _taskOperations.CheckPrereqTasks(task))) {
                        continue;
                    }
                }
            }

            var result = await BuildWorkunit(task);
            if (result == null) {
                continue;
            }

            if (bucketConfig == null) {
                bucketConfig = result.Value.Item1;
            } else if (bucketConfig != result.Value.Item1) {
                throw new Exception($"bucket configs differ: {bucketConfig} VS {result.Value.Item1}");
            }

            work_units.Add(result.Value.Item2);
        }

        if (bucketConfig != null) {
            var setupUrl = await _containers.GetContainerSasUrl(bucketConfig.setupContainer, StorageType.Corpus, BlobContainerSasPermissions.Read | BlobContainerSasPermissions.List) ?? throw new Exception("container not found");
            var workSet = new WorkSet(
                Reboot: bucketConfig.reboot,
                Script: bucketConfig.setupScript != null,
                SetupUrl: setupUrl,
                WorkUnits: work_units
            );

            return (bucketConfig, workSet);
        }

        return null;
    }


    record BucketConfig(int count, bool reboot, Container setupContainer, string? setupScript, Pool pool);

    private async Async.Task<(BucketConfig, WorkUnit)?> BuildWorkunit(Task task) {
        Pool? pool = await _taskOperations.GetPool(task);
        if (pool == null) {
            _logTracer.Info($"unable to find pool for task: {task.TaskId}");
            return null;
        }

        _logTracer.Info($"scheduling task: {task.TaskId}");

        var job = await _jobOperations.Get(task.JobId);

        if (job == null) {
            throw new Exception($"invalid job_id {task.JobId} for task {task.TaskId}");
        }

        var taskConfig = await _config.BuildTaskConfig(job, task);
        var setupContainer = task.Config.Containers?.FirstOrDefault(c => c.Type == ContainerType.Setup) ?? throw new Exception($"task missing setup container: task_type = {task.Config.Task.Type}");

        var setupPs1Exist = _containers.BlobExists(setupContainer.Name, "setup.ps1", StorageType.Corpus);
        var setupShExist = _containers.BlobExists(setupContainer.Name, "setup.sh", StorageType.Corpus);

        string? setupScript = null;
        if (task.Os == Os.Windows && await setupPs1Exist) {
            setupScript = "setup.ps1";
        }

        if (task.Os == Os.Linux && await setupShExist) {
            setupScript = "setup.sh";
        }

        var reboot = false;
        var count = 1;
        if (task.Config.Pool != null) {
            count = task.Config.Pool.Count;
            reboot = task.Config.Task.RebootAfterSetup ?? false;
        } else if (task.Config.Vm != null) {
            count = task.Config.Vm.Count;
            reboot = (task.Config.Vm.RebootAfterSetup ?? false) || (task.Config.Task.RebootAfterSetup ?? false);
        } else {
            throw new Exception();
        }

        var workUnit = new WorkUnit(
            JobId: taskConfig.JobId,
            TaskId: taskConfig.TaskId,
            TaskType: taskConfig.TaskType,
            // todo: make sure that we exclude nulls when serializing
            // config = task_config.json(exclude_none = True, exclude_unset = True),
            Config: taskConfig);

        var bucketConfig = new BucketConfig(
            count,
            reboot,
            setupContainer.Name,
            setupScript,
            pool);



        return (bucketConfig, workUnit);
    }

    record struct BucketId(Os os, Guid jobId, (string, string)? vm, string? pool, string setupContainer, bool? reboot, Guid? unique);

    private ILookup<BucketId, Task> BucketTasks(IEnumerable<Task> tasks) {

        // buckets are hashed by:
        // OS, JOB ID, vm sku & image (if available), pool name (if available),
        // if the setup script requires rebooting, and a 'unique' value
        //
        // The unique value is set based on the following conditions:
        // * if the task is set to run on more than one VM, than we assume it can't be shared
        // * if the task is missing the 'colocate' flag or it's set to False

        return tasks.ToLookup(task => {

            Guid? unique = null;

            // check for multiple VMs for pre-1.0.0 tasks
            (string, string)? vm = task.Config.Vm != null ? (task.Config.Vm.Sku, task.Config.Vm.Image) : null;
            if ((task.Config.Vm?.Count ?? 0) > 1) {
                unique = Guid.NewGuid();
            }

            // check for multiple VMs for 1.0.0 and later tasks
            string? pool = task.Config.Pool?.PoolName;
            if ((task.Config.Pool?.Count ?? 0) > 1) {
                unique = Guid.NewGuid();
            }

            if (!(task.Config.Colocate ?? false)) {
                unique = Guid.NewGuid();
            }

            return new BucketId(task.Os, task.JobId, vm, pool, _config.GetSetupContainer(task.Config), task.Config.Task.RebootAfterSetup, unique);

        });
    }
}


