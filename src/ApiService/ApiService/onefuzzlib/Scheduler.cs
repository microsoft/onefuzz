using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;


public interface IScheduler {
    Async.Task ScheduleTasks();
}

public class Scheduler : IScheduler {
    private readonly ITaskOperations _taskOperations;
    private readonly IConfig _config;
    private readonly IPoolOperations _poolOperations;
    private readonly ILogger _logTracer;
    private readonly IJobOperations _jobOperations;
    private readonly IContainers _containers;

    // TODO: eventually, this should be tied to the pool.
    const int MAX_TASKS_PER_SET = 10;

    public Scheduler(ITaskOperations taskOperations, IConfig config, IPoolOperations poolOperations, ILogger<Scheduler> logTracer, IJobOperations jobOperations, IContainers containers) {
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

        // only fetch pools once from storage; see explanation in BuildWorkUnit for more
        var poolCache = new Dictionary<PoolKey, Pool>();

        foreach (var bucketedTasks in buckets) {
            foreach (var chunks in bucketedTasks.Chunk(MAX_TASKS_PER_SET)) {
                var result = await BuildWorkSet(chunks, poolCache);
                if (result is var (bucketConfig, workSet)) {
                    if (await ScheduleWorkset(workSet, bucketConfig.pool, bucketConfig.count)) {
                        foreach (var workUnit in workSet.WorkUnits) {
                            var task1 = tasks[workUnit.TaskId];
                            Task task = await _taskOperations.SetState(task1, TaskState.Scheduled);
                            _ = seen.Add(task.TaskId);
                        }
                    }
                }
            }
        }

        var notReadyCount = tasks.Count - seen.Count;
        if (notReadyCount > 0) {
            _logTracer.LogInformation("{TasksNotReady} - {TasksSeen}", notReadyCount, seen.Count);
        }
    }

    private async Async.Task<bool> ScheduleWorkset(WorkSet workSet, Pool pool, long count) {
        if (!PoolStateHelper.Available.Contains(pool.State)) {
            _logTracer.LogInformation("pool not available {PoolName} - {PoolState}", pool.Name, pool.State);
            return false;
        }

        for (var i = 0L; i < count; i++) {
            if (!await _poolOperations.ScheduleWorkset(pool, workSet)) {
                _logTracer.LogError("unable to schedule workset {PoolName} {WorkSet}", pool.Name, workSet);
                return false;
            }
        }
        return true;
    }

    private async Async.Task<(BucketConfig, WorkSet)?> BuildWorkSet(Task[] tasks, Dictionary<PoolKey, Pool> poolCache) {
        var taskIds = tasks.Select(x => x.TaskId).ToHashSet();
        var workUnits = new List<WorkUnit>();

        BucketConfig? bucketConfig = null;
        foreach (var task in tasks) {
            if (task.Config.PrereqTasks is List<Guid> prereqTasks && prereqTasks.Any()) {
                // if all of the prereqs are in this bucket, they will be
                // scheduled together
                if (!taskIds.IsSupersetOf(prereqTasks)) {
                    if (!await _taskOperations.CheckPrereqTasks(task)) {
                        continue;
                    }
                }
            }

            var result = await BuildWorkunit(task, poolCache);
            if (result.IsOk) {
                var (newBucketConfig, workUnit) = result.OkV;
                if (bucketConfig is null) {
                    bucketConfig = newBucketConfig;
                } else if (bucketConfig != newBucketConfig) {
                    throw new Exception($"bucket configs differ: {bucketConfig} VS {newBucketConfig}");
                }
                workUnits.Add(workUnit);
            } else {
                await _taskOperations.MarkFailed(task, result.ErrorV);
            }
        }

        if (bucketConfig is BucketConfig c) {
            var readOnlyPermissions = BlobContainerSasPermissions.Read | BlobContainerSasPermissions.List;
            var setupUrl = await _containers.GetContainerSasUrl(c.setupContainer, StorageType.Corpus, readOnlyPermissions);
            var extraSetupUrl = c.extraSetupContainer is not null
                ? await _containers.GetContainerSasUrl(c.extraSetupContainer, StorageType.Corpus, readOnlyPermissions)
                : null;

            var workSet = new WorkSet(
                Reboot: c.reboot,
                Script: c.setupScript is not null,
                SetupUrl: setupUrl,
                ExtraSetupUrl: extraSetupUrl,
                WorkUnits: workUnits
            );

            return (c, workSet);
        }

        return null;
    }


    readonly record struct BucketConfig(
        long count,
        bool reboot,
        Container setupContainer,
        Container? extraSetupContainer,
        string? setupScript,
        Pool pool);

    sealed record PoolKey(
        PoolName? poolName = null,
        (string sku, ImageReference image)? vm = null);

    private static PoolKey? GetPoolKey(Task task) {
        // the behaviour of this key should match the behaviour of TaskOperations.GetPool

        if (task.Config.Pool is TaskPool p) {
            return new PoolKey(poolName: p.PoolName);
        }

        if (task.Config.Vm is TaskVm vm) {
            return new PoolKey(vm: (vm.Sku, vm.Image));
        }

        return null;
    }

    private async Async.Task<OneFuzzResult<(BucketConfig, WorkUnit)>> BuildWorkunit(Task task, Dictionary<PoolKey, Pool> poolCache) {
        var poolKey = GetPoolKey(task);
        if (poolKey is null) {
            return OneFuzzResult<(BucketConfig, WorkUnit)>.Error(ErrorCode.UNABLE_TO_FIND, $"unable to find pool key for the task {task.TaskId} in job {task.JobId}");
        }

        // we cache the pools by key so that we only fetch each pool once
        // this reduces load on storage and also ensures that we don't
        // have multiple copies of the same pool entity with differing values
        if (!poolCache.TryGetValue(poolKey, out var pool)) {
            var foundPool = await _taskOperations.GetPool(task);
            if (foundPool is null) {
                _logTracer.LogInformation("unable to find pool for task: {TaskId}", task.TaskId);
                return OneFuzzResult<(BucketConfig, WorkUnit)>.Error(ErrorCode.UNABLE_TO_FIND, $"unable to find pool for the task {task.TaskId} in job {task.JobId}");
            }

            pool = poolCache[poolKey] = foundPool;
        }

        _logTracer.LogInformation("scheduling task: {TaskId}", task.TaskId);

        var job = await _jobOperations.Get(task.JobId);
        if (job is null) {
            _logTracer.LogError("invalid job {JobId} for task {TaskId}", task.JobId, task.TaskId);
            return OneFuzzResult<(BucketConfig, WorkUnit)>.Error(ErrorCode.INVALID_JOB, $"invalid job_id {task.JobId} for task {task.TaskId}");
        }

        var taskConfig = await _config.BuildTaskConfig(job, task);
        if (taskConfig is null) {
            _logTracer.LogError("unable to build task config for task: {TaskId}", task.TaskId);
            return OneFuzzResult<(BucketConfig, WorkUnit)>.Error(ErrorCode.INVALID_CONFIGURATION, $"unable to build task config for task: {task.TaskId} in job {task.JobId}");
        }
        var setupContainer = task.Config.Containers?.FirstOrDefault(c => c.Type == ContainerType.Setup) ?? throw new Exception($"task missing setup container: task_type = {task.Config.Task.Type}");

        var extraSetupContainer = task.Config.Containers?.FirstOrDefault(c => c is { Type: ContainerType.ExtraSetup });

        string? setupScript = null;
        if (task.Os == Os.Windows) {
            if (await _containers.BlobExists(setupContainer.Name, "setup.ps1", StorageType.Corpus)) {
                setupScript = "setup.ps1";
            }
        }

        if (task.Os == Os.Linux) {
            if (await _containers.BlobExists(setupContainer.Name, "setup.sh", StorageType.Corpus)) {
                setupScript = "setup.sh";
            }
        }

        var reboot = false;
        var count = 1L;
        if (task.Config.Pool is TaskPool p) {
            count = p.Count;
            reboot = task.Config.Task.RebootAfterSetup ?? false;
        } else if (task.Config.Vm is TaskVm vm) {
            count = vm.Count;
            reboot = (vm.RebootAfterSetup ?? false) || (task.Config.Task.RebootAfterSetup ?? false);
        } else {
            return OneFuzzResult<(BucketConfig, WorkUnit)>.Error(ErrorCode.INVALID_CONFIGURATION, $"Either Pool or VM should be set for task: {task.TaskId} in job {task.JobId}");
        }

        var workUnit = new WorkUnit(
            JobId: taskConfig.JobId,
            TaskId: taskConfig.TaskId,
            TaskType: taskConfig.TaskType,
            Env: task.Config.Task.TaskEnv ?? new Dictionary<string, string>(),
            // todo: make sure that we exclude nulls when serializing
            // config = task_config.json(exclude_none = True, exclude_unset = True),
            Config: taskConfig);

        var bucketConfig = new BucketConfig(
            count,
            reboot,
            setupContainer.Name,
            extraSetupContainer?.Name,
            setupScript,
            pool with { ETag = default, Timestamp = default });

        return OneFuzzResult<(BucketConfig, WorkUnit)>.Ok((bucketConfig, workUnit));
    }

    public record struct BucketId(Os os, Guid jobId, (string, ImageReference)? vm, PoolName? pool, Container setupContainer, bool? reboot, Guid? unique);

    public static ILookup<BucketId, Task> BucketTasks(IEnumerable<Task> tasks) {

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
            (string, ImageReference)? vm = task.Config.Vm != null ? (task.Config.Vm.Sku, task.Config.Vm.Image) : null;
            if ((task.Config.Vm?.Count ?? 0) > 1) {
                unique = Guid.NewGuid();
            }

            // check for multiple VMs for 1.0.0 and later tasks
            var pool = task.Config.Pool?.PoolName;
            if ((task.Config.Pool?.Count ?? 0) > 1) {
                unique = Guid.NewGuid();
            }

            if (!(task.Config.Colocate ?? false)) {
                unique = Guid.NewGuid();
            }

            return new BucketId(task.Os, task.JobId, vm, pool, GetSetupContainer(task.Config), task.Config.Task.RebootAfterSetup, unique);

        });
    }

    public static Container GetSetupContainer(TaskConfig config) {

        foreach (var container in config.Containers ?? throw new Exception("Missing containers")) {
            if (container.Type == ContainerType.Setup) {
                return container.Name;
            }
        }

        throw new Exception($"task missing setup container: task_type = {config.Task.Type}");
    }
}
