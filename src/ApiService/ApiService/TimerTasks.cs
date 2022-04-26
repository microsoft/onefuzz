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
        
        await foreach (var job in expiredJobs)
        {
            _logger.Info($"stopping expired job. job_id:{job.JobId }");
            await _jobOperations.Stopping(job);
        }

        //var jobs = _jobOperations.SearchState(states: JobState);
    }
}


