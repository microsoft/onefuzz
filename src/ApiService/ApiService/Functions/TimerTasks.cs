using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service.Functions;

public class TimerTasks {
    private readonly ILogger _logger;


    private readonly ITaskOperations _taskOperations;

    private readonly IJobOperations _jobOperations;

    private readonly IScheduler _scheduler;


    public TimerTasks(ILogger<TimerTasks> logger, ITaskOperations taskOperations, IJobOperations jobOperations, IScheduler scheduler) {
        _logger = logger;
        _taskOperations = taskOperations;
        _jobOperations = jobOperations;
        _scheduler = scheduler;
    }

    [Function("TimerTasks")]
    public async Async.Task Run([TimerTrigger("00:00:15")] TimerInfo myTimer) {
        var expriredTasks = _taskOperations.SearchExpired();
        await foreach (var task in expriredTasks) {
            _logger.LogInformation("stopping expired task. job_id:{JobId} task_id:{TaskId}", task.JobId, task.TaskId);
            await _taskOperations.MarkStopping(task, "task is expired");
        }


        var expiredJobs = _jobOperations.SearchExpired();

        await foreach (var job in expiredJobs) {
            _logger.LogInformation("stopping expired job. job_id:{JobId}", job.JobId);
            _ = await _jobOperations.Stopping(job);
        }

        var jobs = _jobOperations.SearchState(states: JobStateHelper.NeedsWork);

        await foreach (var job in jobs) {
            _logger.LogInformation("update job: {JobId}", job.JobId);
            _ = await _jobOperations.ProcessStateUpdates(job);
        }

        var tasks = _taskOperations.SearchStates(states: TaskStateHelper.NeedsWorkStates);
        await foreach (var task in tasks) {
            _logger.LogInformation("update task: {TaskId}", task.TaskId);
            _ = await _taskOperations.ProcessStateUpdate(task);
        }

        await _scheduler.ScheduleTasks();

        await _jobOperations.StopNeverStartedJobs();
    }
}
