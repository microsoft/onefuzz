using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface ITaskOperations : IOrm<Task>
{
    Async.Task<Task?> GetByTaskId(Guid taskId);
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

}
