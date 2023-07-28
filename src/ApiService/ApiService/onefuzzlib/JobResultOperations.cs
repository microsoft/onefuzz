using ApiService.OneFuzzLib.Orm;
using Microsoft.Extensions.Logging;
using Polly;
namespace Microsoft.OneFuzz.Service;

public interface IJobResultOperations : IOrm<JobResult> {

    Async.Task<JobResult?> GetJobResult(Guid jobId);
    JobResult UpdateResult(JobResult result, HeartbeatType type);
    Async.Task<bool> TryUpdate(Job job, HeartbeatType resultType);
    Async.Task<OneFuzzResult<bool>> CreateOrUpdate(Guid jobId, HeartbeatType resultType);

}
public class JobResultOperations : Orm<JobResult>, IJobResultOperations {

    public JobResultOperations(ILogger<JobResultOperations> log, IOnefuzzContext context)
        : base(log, context) {
    }

    public async Async.Task<JobResult?> GetJobResult(Guid jobId) {
        return await SearchByPartitionKeys(new[] { jobId.ToString() }).SingleOrDefaultAsync();
    }

    public JobResult UpdateResult(JobResult result, HeartbeatType type) {

        var newResult = result;
        int newValue;
        switch (type) {
            case HeartbeatType.NewCrashingInput:
                newValue = result.NewCrashingInput + 1;
                newResult = result with { NewCrashingInput = newValue };
                break;
            case HeartbeatType.NewReport:
                newValue = result.NewReport + 1;
                newResult = result with { NewReport = newValue };
                break;
            case HeartbeatType.NewUniqueReport:
                newValue = result.NewUniqueReport + 1;
                newResult = result with { NewUniqueReport = newValue };
                break;
            case HeartbeatType.NewRegressionReport:
                newValue = result.NewRegressionReport + 1;
                newResult = result with { NewRegressionReport = newValue };
                break;
            default:
                _logTracer.LogInformation("Invalid Field.");
                break;
        }

        return newResult;
    }

    public async Async.Task<bool> TryUpdate(Job job, HeartbeatType resultType) {
        var jobId = job.JobId;
        var jobResult = await GetJobResult(jobId);

        if (jobResult == null) {
            _logTracer.LogInformation("Creating new JobResult for Job {JobId}", jobId);

            var entry = new JobResult(JobId: jobId, Project: job.Config.Project, Name: job.Config.Name, 0, 0, 0, 0, 0);

            jobResult = UpdateResult(entry, resultType);

            var r = await Insert(jobResult);
            if (!r.IsOk) {
                _logTracer.AddHttpStatus(r.ErrorV);
                _logTracer.LogError("failed to insert job result {JobId}", jobResult.JobId);
                throw new InvalidOperationException($"failed to insert job result {jobResult.JobId}");
            }
            _logTracer.LogInformation("created job result {JobId}", jobResult.JobId);
        } else {
            _logTracer.LogInformation("Updating existing JobResult entry for Job {JobId}", jobId);

            jobResult = UpdateResult(jobResult, resultType);

            var r = await Update(jobResult);
            if (!r.IsOk) {
                _logTracer.AddHttpStatus(r.ErrorV);
                _logTracer.LogError("failed to update job result {JobId}", jobResult.JobId);
                throw new InvalidOperationException($"failed to insert job result {jobResult.JobId}");
            }
            _logTracer.LogInformation("updated job result {JobId}", jobResult.JobId);
        }

        return true;
    }

    public async Async.Task<OneFuzzResult<bool>> CreateOrUpdate(Guid jobId, HeartbeatType resultType) {

        var job = await _context.JobOperations.Get(jobId);
        if (job == null) {
            return OneFuzzResult<bool>.Error(ErrorCode.INVALID_REQUEST, "invalid job");
        }

        var success = false;
        try {
            _logTracer.LogInformation("attempt to update job result {JobId}", job.JobId);
            var policy = Policy.Handle<InvalidOperationException>().WaitAndRetryAsync(50, _ => new TimeSpan(0, 0, 5));
            await policy.ExecuteAsync(async () => {
                success = await TryUpdate(job, resultType);
            });
            return OneFuzzResult<bool>.Ok(success);
        } catch (Exception e) {
            return OneFuzzResult<bool>.Error(ErrorCode.UNABLE_TO_UPDATE, new string[] {
                    $"Unexpected failure when attempting to update job result for {job.JobId}",
                    $"Exception: {e}"
                });
        }
    }
}

