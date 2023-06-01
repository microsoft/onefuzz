using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IJobOperations : IStatefulOrm<Job, JobState> {
    Async.Task<Job?> Get(Guid jobId);
    Async.Task OnStart(Job job);
    IAsyncEnumerable<Job> SearchExpired();
    IAsyncEnumerable<Job> SearchState(IEnumerable<JobState> states);
    Async.Task StopNeverStartedJobs();
    Async.Task StopIfAllDone(Job job);

    // state transitions
    Async.Task<Job> Init(Job job);
    Async.Task<Job> Enabled(Job job);
    Async.Task<Job> Stopping(Job job);
    Async.Task<Job> Stopped(Job job);
}

public class JobOperations : StatefulOrm<Job, JobState, JobOperations>, IJobOperations {
    private static readonly TimeSpan JOB_NEVER_STARTED_DURATION = TimeSpan.FromDays(30);

    public JobOperations(ILogTracer logTracer, IOnefuzzContext context) : base(logTracer, context) {
    }

    public async Async.Task<Job?> Get(Guid jobId) {
        return await QueryAsync(Query.PartitionKey(jobId.ToString())).FirstOrDefaultAsync();
    }

    public async Async.Task OnStart(Job job) {
        if (job.EndTime == null) {
            var r = await Replace(job with { EndTime = DateTimeOffset.UtcNow + TimeSpan.FromHours(job.Config.Duration) });
            if (!r.IsOk) {
                _logTracer.WithHttpStatus(r.ErrorV).Error($"failed to replace job {job.JobId:Tag:JobId} when calling OnStart");
            }
        }
    }

    public IAsyncEnumerable<Job> SearchExpired() {
        var timeFilter = Query.OlderThan("end_time", DateTimeOffset.UtcNow);
        var stateFilter = Query.EqualAnyEnum("state", JobStateHelper.Available);
        var filter = Query.And(stateFilter, timeFilter);
        return QueryAsync(filter: filter);
    }

    public IAsyncEnumerable<Job> SearchState(IEnumerable<JobState> states) {
        var query = Query.EqualAnyEnum("state", states);
        return QueryAsync(filter: query);
    }

    public async Async.Task StopIfAllDone(Job job) {

        var tasks = _context.TaskOperations.GetByJobId(job.JobId);

        if (!await tasks.AnyAsync()) {
            _logTracer.Warning($"StopIfAllDone could not find any tasks for job with id {job.JobId:Tag:JobId}");
        }

        var anyNotStoppedTasks = await tasks.AnyAsync(task => task.State != TaskState.Stopped);

        if (anyNotStoppedTasks) {
            return;
        }

        _logTracer.Info($"stopping job as all tasks are stopped: {job.JobId:Tag:JobId}");
        _ = await Stopping(job);
    }

    public async Async.Task StopNeverStartedJobs() {
        // # Note, the "not(end_time...)" with end_time set long before the use of
        // # OneFuzz enables identifying those without end_time being set.

        var lastTimeStamp = (DateTimeOffset.UtcNow - JOB_NEVER_STARTED_DURATION).ToString("o");

        var filter = Query.And(new[] {
            $"Timestamp lt datetime'{lastTimeStamp}' and not(end_time ge datetime'2000-01-11T00:00:00.0Z')",
            Query.EqualEnum("state", JobState.Enabled)
        });

        await foreach (var job in QueryAsync(filter)) {
            await foreach (var task in _context.TaskOperations.QueryAsync(Query.PartitionKey(job.JobId.ToString()))) {
                await _context.TaskOperations.MarkFailed(task, Error.Create(ErrorCode.TASK_FAILED, "job never not start"));
            }
            _logTracer.Info($"stopping job that never started: {job.JobId:Tag:JobId}");

            // updated result ignored: not used after this loop
            _ = await _context.JobOperations.Stopping(job);
        }
    }

    public async Async.Task<Job> Init(Job job) {
        _logTracer.Info($"init job: {job.JobId:Tag:JobId}");
        var enabled = job with { State = JobState.Enabled };
        var result = await Replace(enabled);
        if (result.IsOk) {
            return enabled;
        } else {
            _logTracer.WithHttpStatus(result.ErrorV).Error($"Failed to save job when init {job.JobId:Tag:JobId} : {result.ErrorV:Tag:Error}");
            throw new Exception($"Failed to save job when init {job.JobId} : {result.ErrorV}");
        }
    }

    public async Async.Task<Job> Stopping(Job job) {
        job = job with { State = JobState.Stopping };
        var tasks = await _context.TaskOperations.QueryAsync(Query.PartitionKey(job.JobId.ToString())).ToListAsync();
        var taskNotStopped = tasks.ToLookup(task => task.State != TaskState.Stopped);

        var notStopped = taskNotStopped[true];
        var stopped = taskNotStopped[false];

        if (notStopped.Any()) {
            foreach (var task in notStopped) {
                await _context.TaskOperations.MarkStopping(task, "job is stopping");
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
            _logTracer.WithHttpStatus(result.ErrorV).Error($"Failed to save job when stopping {job.JobId:Tag:JobId} : {result.ErrorV:Tag:Error}");
            throw new Exception($"Failed to save job when stopping {job.JobId} : {result.ErrorV}");
        }
    }

    public Task<Job> Enabled(Job job) {
        // nothing to do
        return Async.Task.FromResult(job);
    }

    public Task<Job> Stopped(Job job) {
        // nothing to do
        return Async.Task.FromResult(job);
    }
}
