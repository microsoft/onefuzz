using Microsoft.Azure.Functions.Worker;

namespace Microsoft.OneFuzz.Service.Functions;


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

    [Function("TimerTasks")]
    public async Async.Task Run([TimerTrigger("00:00:15")] TimerInfo myTimer) {
        var expriredTasks = _taskOperations.SearchExpired();
        await foreach (var task in expriredTasks) {
            _logger.Info($"stopping expired task. job_id:{task.JobId:Tag:JobId} task_id:{task.TaskId:Tag:TaskId}");
            await _taskOperations.MarkStopping(task);
        }


        var expiredJobs = _jobOperations.SearchExpired();

        await foreach (var job in expiredJobs) {
            _logger.Info($"stopping expired job. job_id:{job.JobId:Tag:JobId}");
            _ = await _jobOperations.Stopping(job);
        }

        var jobs = _jobOperations.SearchState(states: JobStateHelper.NeedsWork);

        await foreach (var job in jobs) {
            _logger.Info($"update job: {job.JobId:Tag:JobId}");
            _ = await _jobOperations.ProcessStateUpdates(job);
        }

        var tasks = _taskOperations.SearchStates(states: TaskStateHelper.NeedsWorkStates);
        await foreach (var task in tasks) {
            _logger.Info($"update task: {task.TaskId:Tag:TaskId}");
            _ = await _taskOperations.ProcessStateUpdate(task);
        }

        await _scheduler.ScheduleTasks();

        await _jobOperations.StopNeverStartedJobs();
    }
}
