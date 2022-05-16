using Microsoft.Azure.Functions.Worker;

namespace Microsoft.OneFuzz.Service;


public class TimerTasks {
    private readonly ILogTracer _logger;


    private readonly ITaskOperations _taskOperations;

    private readonly IJobOperations _jobOperations;

    private readonly IScheduler _scheduler;


    public TimerTasks(ILogTracer logger, ITaskOperations taskOperations, IJobOperations jobOperations, IScheduler scheduler) {
        _logger = logger;
        _taskOperations = taskOperations;
        _jobOperations = jobOperations;
        _scheduler = scheduler;
    }

    //[Function("TimerTasks")]
    public async Async.Task Run([TimerTrigger("1.00:00:00")] TimerInfo myTimer) {
        var expriredTasks = _taskOperations.SearchExpired();
        await foreach (var task in expriredTasks) {
            _logger.Info($"stopping expired task. job_id:{task.JobId} task_id:{task.TaskId}");
            await _taskOperations.MarkStopping(task);
        }


        var expiredJobs = _jobOperations.SearchExpired();

        await foreach (var job in expiredJobs) {
            _logger.Info($"stopping expired job. job_id:{job.JobId}");
            await _jobOperations.Stopping(job, _taskOperations);
        }

        var jobs = _jobOperations.SearchState(states: JobStateHelper.NeedsWork);

        await foreach (var job in jobs) {
            _logger.Info($"update job: {job.JobId}");
            await _jobOperations.ProcessStateUpdates(job);
        }

        var tasks = _taskOperations.SearchStates(states: TaskStateHelper.NeedsWork);
        await foreach (var task in tasks) {
            _logger.Info($"update task: {task.TaskId}");
            await _taskOperations.ProcessStateUpdate(task);
        }

        await _scheduler.ScheduleTasks();

        await _jobOperations.StopNeverStartedJobs();
    }
}


