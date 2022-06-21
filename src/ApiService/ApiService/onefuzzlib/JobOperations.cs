using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IJobOperations : IStatefulOrm<Job, JobState> {
    Async.Task<Job?> Get(Guid jobId);
    Async.Task OnStart(Job job);
    IAsyncEnumerable<Job> SearchExpired();
    Async.Task<Job> Stopping(Job job);
    IAsyncEnumerable<Job> SearchState(IEnumerable<JobState> states);
    Async.Task StopNeverStartedJobs();
    Async.Task StopIfAllDone(Job job);
}

public class JobOperations : StatefulOrm<Job, JobState>, IJobOperations {
    private static TimeSpan JOB_NEVER_STARTED_DURATION = TimeSpan.FromDays(30);

    public JobOperations(ILogTracer logTracer, IOnefuzzContext context) : base(logTracer, context) {
    }

    public async Async.Task<Job?> Get(Guid jobId) {
        return await QueryAsync($"PartitionKey eq '{jobId}'").FirstOrDefaultAsync();
    }

    public async Async.Task OnStart(Job job) {
        if (job.EndTime == null) {
            await Replace(job with { EndTime = DateTimeOffset.UtcNow + TimeSpan.FromHours(job.Config.Duration) });
        }
    }

    public IAsyncEnumerable<Job> SearchExpired() {
        return QueryAsync(filter: $"end_time lt datetime'{DateTimeOffset.UtcNow.ToString("o")}'");
    }

    public IAsyncEnumerable<Job> SearchState(IEnumerable<JobState> states) {
        var query = Query.EqualAnyEnum("state", states);
        return QueryAsync(filter: query);
    }

    public async Async.Task StopIfAllDone(Job job) {
        var anyNotStoppedJobs = await _context.TaskOperations.GetByJobId(job.JobId).AnyAsync(job => job.State != TaskState.Stopped);

        if (!anyNotStoppedJobs) {
            return;
        }

        _logTracer.Info($"stopping job as all tasks are stopped: {job.JobId}");
        await Stopping(job);
    }

    public async Async.Task StopNeverStartedJobs() {
        // # Note, the "not(end_time...)" with end_time set long before the use of
        // # OneFuzz enables identifying those without end_time being set.

        var lastTimeStamp = (DateTimeOffset.UtcNow - JOB_NEVER_STARTED_DURATION).ToString("o");

        var filter = Query.And(new[] {
            $"Timestamp lt datetime'{lastTimeStamp}' and not(end_time ge datetime'2000-01-11T00:00:00.0Z')",
            Query.EqualAnyEnum("state", new[] {JobState.Enabled})
        });

        var jobs = this.QueryAsync(filter);

        await foreach (var job in jobs) {
            await foreach (var task in _context.TaskOperations.QueryAsync($"PartitionKey eq '{job.JobId}'")) {
                await _context.TaskOperations.MarkFailed(task, new Error(ErrorCode.TASK_FAILED, new[] { "job never not start" }));
            }
            _logTracer.Info($"stopping job that never started: {job.JobId}");
            await _context.JobOperations.Stopping(job);
        }
    }

    public async Async.Task<Job> Stopping(Job job) {
        job = job with { State = JobState.Stopping };
        var tasks = await _context.TaskOperations.QueryAsync(filter: $"job_id eq '{job.JobId}'").ToListAsync();
        var taskNotStopped = tasks.ToLookup(task => task.State != TaskState.Stopped);

        var notStopped = taskNotStopped[true];
        var stopped = taskNotStopped[false];

        if (notStopped.Any()) {
            foreach (var task in notStopped) {
                await _context.TaskOperations.MarkStopping(task);
            }
        } else {
            job = job with { State = JobState.Stopped };
            var taskInfo = stopped.Select(t => new JobTaskStopped(t.TaskId, t.Config.Task.Type, t.Error)).ToList();
            await _context.Events.SendEvent(new EventJobStopped(job.JobId, job.Config, job.UserInfo, taskInfo));
        }

        var result = await Replace(job);

        if (result.IsOk) {
            return job;
        } else {
            throw new Exception($"Failed to save job {job.JobId}");
        }
    }
}
