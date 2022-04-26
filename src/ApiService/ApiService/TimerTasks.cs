using ApiService.OneFuzzLib.Orm;
using Microsoft.Azure.Functions.Worker;

namespace Microsoft.OneFuzz.Service;


public class TimerTasks
{
    private readonly ILogTracer _logger;

    private readonly IScalesetOperations _scalesets;

    private readonly ITaskOperations _taskOperations;

    private readonly IJobOperations _jobOperations;


    public TimerTasks(ILogTracer logger, IScalesetOperations scalesets, ITaskOperations taskOperations, IJobOperations jobOperations)
    {
        _logger = logger;
        _scalesets = scalesets;
        _taskOperations = taskOperations;
        _jobOperations = jobOperations;
    }

    //[Function("TimerTasks")]
    public async Async.Task Run([TimerTrigger("1.00:00:00")] TimerInfo myTimer)
    {
        var expriredTasks = _taskOperations.SearchExpired();
        await foreach (var task in expriredTasks)
        {
            _logger.Info($"stopping expired task. job_id:{task.JobId} task_id:{task.TaskId}");
            await _taskOperations.MarkStopping(task);
        }


        var expiredJobs = _jobOperations.SearchExpired();
        
        foreach (var job in expiredJobs)
        {
            _logger.Info($"stopping expired job. job_id:{job.JobId }");
            await _jobOperations.Stopping(job);
        }
    }
}


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

    public async  System.Threading.Tasks.Task OnStart(Job job)
    {
        if (job.EndTime == null) {
            await Replace(job with { EndTime = DateTimeOffset.UtcNow + TimeSpan.FromHours(job.Config.Duration) });
        }
    }

    public IAsyncEnumerable<Job> SearchExpired()
    {
        return QueryAsync(filter: $"end_time lt datetime'{DateTimeOffset.UtcNow}'");
    }

    public async System.Threading.Tasks.Task Stopping(Job job)
    {
        job = job with {State = JobState.Stopping};
        var tasks = await _taskOperations.QueryAsync(filter: $"job_id eq '{job.JobId}'").ToListAsync();
        var taskNotStopped = tasks.ToLookup(task => task.State != TaskState.Stopped);

        var notStopped  = taskNotStopped[true];
        var stopped = taskNotStopped[false];

        if (notStopped.Any())
        {
            foreach (var task in notStopped) { 
                await _taskOperations.MarkStopping(task);
            }
        } else
        {
            job = job with { State = JobState.Stopped };
            var taskInfo = stopped.Select(t => new JobTaskStopped(t.TaskId, t.Config.Task.Type, t.Error)).ToList();
            await _events.SendEvent(new EventJobStopped(job.JobId, job.Config, job.UserInfo, taskInfo));
        }

        await Replace(job);

    }
}
