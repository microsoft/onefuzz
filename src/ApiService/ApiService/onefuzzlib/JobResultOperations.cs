using ApiService.OneFuzzLib.Orm;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;

public interface IJobResultOperations : IOrm<JobResult> {

    Async.Task<JobResult?> GetJobResult(Guid jobId);
    JobResult UpdateResult(JobResult result, HeartbeatType type);
    Async.Task<OneFuzzResult<JobResult>> CreateOrUpdate(Guid jobId, HeartbeatType resultType);

}
public class JobResultOperations : Orm<JobResult>, IJobResultOperations {

    public JobResultOperations(ILogger<JobResultOperations> log, IOnefuzzContext context)
        : base(log, context) {
    }

    public async Async.Task<JobResult?> GetJobResult(Guid jobId) {
        return await SearchByPartitionKeys(new[] { jobId.ToString() }).SingleOrDefaultAsync();
    }

    public JobResult UpdateResult(JobResult result, HeartbeatType type) {
        // return await SearchByPartitionKeys(new[] { jobId.ToString() }).SingleOrDefaultAsync();

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

    public async Async.Task<Boolean> TryUpdate(Job job, HeartbeatType resultType) {
        // return await SearchByPartitionKeys(new[] { jobId.ToString() }).SingleOrDefaultAsync();
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
                return false;
            }
            _logTracer.LogInformation("created job result {JobId}", jobResult.JobId);
        } else {
            _logTracer.LogInformation("Updating existing JobResult entry for Job {JobId}", jobId);

            jobResult = UpdateResult(jobResult, resultType);

            var r = await Update(jobResult);
            if (!r.IsOk) {
                _logTracer.AddHttpStatus(r.ErrorV);
                _logTracer.LogError("failed to update job result {JobId}", jobResult.JobId);
                return false;
            }
            _logTracer.LogInformation("updated job result {JobId}", jobResult.JobId);
        }

        return true;
    }

    public async Async.Task<OneFuzzResult<Boolean>> CreateOrUpdate(Guid jobId, HeartbeatType resultType) {

        var job = await _context.JobOperations.Get(jobId);
        if (job == null) {
            return OneFuzzResult<Boolean>.Error(ErrorCode.INVALID_REQUEST, "invalid job");
        }

        var retries = 0;
        var success = false;
        while (!success || retries < 10) {
            _logTracer.LogInformation("attempt {retries} to update job result {JobId}", retries, job.JobId);
            success = await TryUpdate(job, resultType);
            retries++;
        }

        return OneFuzzResult<Boolean>.Ok(success);
    }
}

