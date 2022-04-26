namespace Microsoft.OneFuzz.Service;


public interface IScheduler {
    Async.Task ScheduleTasks();
}

public class Scheduler : IScheduler {
    private readonly ITaskOperations _taskOperations;
    private readonly IConfig _config;

    // TODO: eventually, this should be tied to the pool.
    const int MAX_TASKS_PER_SET = 10;

    public Scheduler(ITaskOperations taskOperations, IConfig config) {
        _taskOperations = taskOperations;
        _config = config;
    }

    public async System.Threading.Tasks.Task ScheduleTasks() {
        var tasks = await _taskOperations.SearchStates(states: new[] { TaskState.Waiting }).ToDictionaryAsync(x => x.TaskId);
        var seen = new HashSet<Guid>();

        var buckets = BucketTasks(tasks.Values);

        foreach (var bucketedTasks in buckets) {
            foreach (var chunks in bucketedTasks.Chunk(MAX_TASKS_PER_SET)) {
                var result = BuildWorkSet(chunks);
            }
        }

        throw new NotImplementedException();
    }

    private object BuildWorkSet(Task[] chunks) {
        throw new NotImplementedException();
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


public interface IConfig {
    string GetSetupContainer(TaskConfig config);
}

public class Config : IConfig {
    public string GetSetupContainer(TaskConfig config) {

        foreach (var container in config.Containers ?? throw new Exception("Missing containers")) {
            if (container.Type == ContainerType.Setup) {
                return container.Name.ContainerName;
            }
        }

        throw new Exception($"task missing setup container: task_type = {config.Task.Type}");
    }
}
