using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IJobOperations : IStatefulOrm<Job, JobState>
{
    System.Threading.Tasks.Task<Job?> Get(Guid jobId);
    System.Threading.Tasks.Task OnStart(Job job);
    IAsyncEnumerable<Job> SearchExpired();
    System.Threading.Tasks.Task Stopping(Job job);
}

public class JobOperations : StatefulOrm<Job, JobState>, IJobOperations
{
    private readonly ITaskOperations _taskOperations;
    private readonly IEvents _events;

    public JobOperations(IStorage storage, ILogTracer logTracer, IServiceConfig config, ITaskOperations taskOperations, IEvents events) : base(storage, logTracer, config)
    {
        _taskOperations = taskOperations;
        _events = events;
    }

    public async System.Threading.Tasks.Task<Job?> Get(Guid jobId)
    {
        return await QueryAsync($"PartitionKey eq '{jobId}'").FirstOrDefaultAsync();
    }

    public async System.Threading.Tasks.Task OnStart(Job job)
    {
        if (job.EndTime == null)
        {
            await Replace(job with { EndTime = DateTimeOffset.UtcNow + TimeSpan.FromHours(job.Config.Duration) });
        }
    }

    public IAsyncEnumerable<Job> SearchExpired()
    {
        return QueryAsync(filter: $"end_time lt datetime'{DateTimeOffset.UtcNow}'");
    }

    public async System.Threading.Tasks.Task Stopping(Job job)
    {
        job = job with { State = JobState.Stopping };
        var tasks = await _taskOperations.QueryAsync(filter: $"job_id eq '{job.JobId}'").ToListAsync();
        var taskNotStopped = tasks.ToLookup(task => task.State != TaskState.Stopped);

        var notStopped = taskNotStopped[true];
        var stopped = taskNotStopped[false];

        if (notStopped.Any())
        {
            foreach (var task in notStopped)
            {
                await _taskOperations.MarkStopping(task);
            }
        }
        else
        {
            job = job with { State = JobState.Stopped };
            var taskInfo = stopped.Select(t => new JobTaskStopped(t.TaskId, t.Config.Task.Type, t.Error)).ToList();
            await _events.SendEvent(new EventJobStopped(job.JobId, job.Config, job.UserInfo, taskInfo));
        }

        await Replace(job);

    }
}
