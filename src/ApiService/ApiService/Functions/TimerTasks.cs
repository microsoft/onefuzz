using System.Threading.Tasks;
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
        // perform up to 10 updates in parallel for each entity type
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 10 };

        var expiredTasks = _taskOperations.SearchExpired();
        // marking one task stopping can also mark other tasks in the same job as failed
        // this could make parallel updates stomp on each other, so don't do it in parallel
        await foreach (var task in expiredTasks) {
            _logger.LogInformation("stopping expired task. job_id:{JobId} task_id:{TaskId}", task.JobId, task.TaskId);
            await _taskOperations.MarkStopping(task, "task is expired");
        }

        var expiredJobs = _jobOperations.SearchExpired();
        // job updates are all distinct and only update tasks owned by that job, can be performed in parallel
        await Parallel.ForEachAsync(expiredJobs, parallelOptions, async (job, _cancel) => {
            _logger.LogInformation("stopping expired job. job_id:{JobId}", job.JobId);
            _ = await _jobOperations.Stopping(job);
        });

        var jobs = _jobOperations.SearchState(states: JobStateHelper.NeedsWork);
        // job updates are okay to do in parallel
        await Parallel.ForEachAsync(jobs, parallelOptions, async (job, _cancel) => {
            _logger.LogInformation("update job: {JobId}", job.JobId);
            _ = await _jobOperations.ProcessStateUpdates(job);
        });

        var tasks = _taskOperations.SearchStates(states: TaskStateHelper.NeedsWorkStates);
        // task state transitions might affect the job, so parallel updates could stomp on each other
        await foreach (var task in tasks) {
            _logger.LogInformation("update task: {TaskId}", task.TaskId);
            _ = await _taskOperations.ProcessStateUpdate(task);
        }

        await _scheduler.ScheduleTasks();
        await _jobOperations.StopNeverStartedJobs();
    }
}
