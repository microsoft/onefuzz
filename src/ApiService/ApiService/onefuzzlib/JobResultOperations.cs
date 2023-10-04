using ApiService.OneFuzzLib.Orm;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;

public interface IJobResultOperations : IOrm<JobResult> {

    Async.Task<OneFuzzResultVoid> CreateOrUpdate(Guid jobId, Guid taskId, Guid machineId, string resultType, Dictionary<string, double> resultValue);

}
public class JobResultOperations : Orm<JobResult>, IJobResultOperations {

    public JobResultOperations(ILogger<JobResultOperations> log, IOnefuzzContext context)
        : base(log, context) {
    }

    private async Async.Task<bool> InsertEntry(Job job, Guid taskId, Guid machineId, string resultType, Dictionary<string, double> resultValue) {
        var jobId = job.JobId;

        _logTracer.LogInformation("Creating new JobResult for Job {JobId}", jobId);

        var entry = new JobResult(ResultId: Guid.NewGuid(), JobId: jobId, TaskId: taskId, MachineId: machineId, Project: job.Config.Project, Name: job.Config.Name, Type: resultType, MetricValue: resultValue);

        // do we need retries for job results? 
        var r = await Insert(entry);
        if (!r.IsOk) {
            throw new InvalidOperationException($"failed to insert job result {jobId}");
        }
        _logTracer.LogInformation("created job result {JobId}", jobId);

        return true;
    }

    public async Async.Task<OneFuzzResultVoid> CreateOrUpdate(Guid jobId, Guid taskId, Guid machineId, string resultType, Dictionary<string, double> resultValue) {

        var job = await _context.JobOperations.Get(jobId);
        if (job == null) {
            return OneFuzzResultVoid.Error(ErrorCode.INVALID_REQUEST, "invalid job");
        }

        bool success;
        _logTracer.LogInformation("attempt to update job result table with entry for {JobId}", job.JobId);
        success = await InsertEntry(job, taskId, machineId, resultType, resultValue);
        _logTracer.LogInformation("attempt {success}", success);

        if (success) {
            return OneFuzzResultVoid.Ok;
        } else {
            return OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_UPDATE, new string[] {
                    $"Unexpected failure when attempting to update job result for {job.JobId}"
                });
        }

    }
}

