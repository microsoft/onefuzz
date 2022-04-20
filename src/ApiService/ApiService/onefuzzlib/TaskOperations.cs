using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface ITaskOperations : IOrm<Task>
{
    Async.Task<Task?> GetByTaskId(Guid taskId);

    Async.Task<Task?> GetByJobIdAndTaskId(Guid jobId, Guid taskId);


    IAsyncEnumerable<Task> SearchStates(Guid? jobId = null, IEnumerable<TaskState>? states = null);

    IEnumerable<string>? GetInputContainerQueues(TaskConfig config);

}

public class TaskOperations : Orm<Task>, ITaskOperations
{

    public TaskOperations(IStorage storage)
        : base(storage)
    {

    }

    public async Async.Task<Task?> GetByTaskId(Guid taskId)
    {
        var data = QueryAsync(filter: $"RowKey eq '{taskId}'");

        return await data.FirstOrDefaultAsync();
    }

    public async Async.Task<Task?> GetByJobIdAndTaskId(Guid jobId, Guid taskId)
    {
        var data = QueryAsync(filter: $"PartitionKey eq '{jobId}' and RowKey eq '{taskId}'");

        return await data.FirstOrDefaultAsync();
    }
    public IAsyncEnumerable<Task> SearchStates(Guid? jobId = null, IEnumerable<TaskState>? states = null)
    {
        var queryString = String.Empty;
        if (jobId != null)
        {
            queryString += $"PartitionKey eq '{jobId}'";
        }

        if (states != null)
        {
            if (jobId != null)
            {
                queryString += " and ";
            }

            var statesString = string.Join(",", states);
            queryString += $"state in ({statesString})";
        }

        return QueryAsync(filter: queryString);
    }

    public IEnumerable<string>? GetInputContainerQueues(TaskConfig config)
    {
        throw new NotImplementedException();
    }

}
