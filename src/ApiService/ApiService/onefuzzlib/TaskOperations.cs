using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface ITaskOperations : IStatefulOrm<Task, TaskState> {
    Async.Task<Task?> GetByTaskId(Guid taskId);

    Async.Task<Task?> GetByJobIdAndTaskId(Guid jobId, Guid taskId);


    IAsyncEnumerable<Task> SearchStates(Guid? jobId = null, IEnumerable<TaskState>? states = null);

    IEnumerable<string>? GetInputContainerQueues(TaskConfig config);

    Async.Task<TaskVm?> GetReproVmConfig(Task task);
}

public class TaskOperations : StatefulOrm<Task, TaskState>, ITaskOperations {

    private IPoolOperations _poolOperations;
    private IScalesetOperations _scalesetOperations;

    public TaskOperations(IStorage storage, ILogTracer log, IServiceConfig config, IPoolOperations poolOperations, IScalesetOperations scalesetOperations)
        : base(storage, log, config) {
        _poolOperations = poolOperations;
        _scalesetOperations = scalesetOperations;
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

}
