using ApiService.OneFuzzLib.Orm;
using Microsoft.Extensions.Logging;
using Polly;
namespace Microsoft.OneFuzz.Service;
using System.Net;

public interface IJobResultOperations : IOrm<JobResult> {

    Async.Task<JobResult?> GetJobResult(Guid jobId, Guid taskId, Guid machineId, string metricType);
    Async.Task<JobResult?> GetJobResults(Guid jobId);
    Async.Task<OneFuzzResultVoid> CreateOrUpdate(Guid jobId, Guid taskId, Guid machineId, DateTime createdAt, string resultType, Dictionary<string, double> resultValue);

}
public class JobResultOperations : Orm<JobResult>, IJobResultOperations {

    const string COVERAGE_DATA = "CoverageData";
    const string RUNTIME_STATS = "RuntimeStats";

    public JobResultOperations(ILogger<JobResultOperations> log, IOnefuzzContext context)
        : base(log, context) {
    }

    public async Async.Task<JobResult?> GetJobResult(Guid jobId, Guid taskId, Guid machineId, string metricType) {
        var data = QueryAsync(Query.SingleEntity(jobId.ToString(), string.Concat(taskId, machineId, metricType)));
        return await data.FirstOrDefaultAsync();
    }

    public async Async.Task<JobResult?> GetJobResults(Guid jobId) {
        var data = QueryAsync(Query.PartitionKey(jobId.ToString()));
        return await data.FirstOrDefaultAsync();
    }

    private async Async.Task<bool> TryUpdate(Job job, Guid taskId, Guid machineId, DateTime createdAt, string resultType, Dictionary<string, double> resultValue) {
        var jobId = job.JobId;
        var taskIdMachineIdMetric = string.Concat(taskId, machineId, resultType);

        var oldEntry = await GetJobResult(jobId, taskId, machineId, resultType);

        if (oldEntry == null) {
            _logTracer.LogInformation($"attempt to insert new job result {taskId} and taskId+machineId+metricType {taskIdMachineIdMetric}");
            var newEntry = new JobResult(JobId: jobId, TaskIdMachineIdMetric: taskIdMachineIdMetric, TaskId: taskId, MachineId: machineId, CreatedAt: createdAt, Project: job.Config.Project, Name: job.Config.Name, resultType, resultValue);
            var result = await Insert(newEntry);
            if (!result.IsOk) {
                throw new InvalidOperationException($"failed to insert job result with taskId {taskId} and taskId+machineId+metricType {taskIdMachineIdMetric}");
            }
            return true;
        }

        ResultVoid<(HttpStatusCode Status, string Reason)> r;
        switch (resultType) {
            case COVERAGE_DATA:
            case RUNTIME_STATS:
                if (oldEntry.CreatedAt < createdAt) {
                    oldEntry = oldEntry with { MetricValue = resultValue };
                    r = await Update(oldEntry);
                    if (!r.IsOk) {
                        throw new InvalidOperationException($"failed to replace job result with taskId {taskId} and machineId+metricType {taskIdMachineIdMetric}");
                    }
                }
                break;
            default:
                _logTracer.LogInformation($"attempt to update job result {taskId} and taskId+machineId+metricType {taskIdMachineIdMetric}");
                oldEntry.MetricValue["count"]++;
                oldEntry = oldEntry with { MetricValue = oldEntry.MetricValue };
                r = await Update(oldEntry);
                if (!r.IsOk) {
                    throw new InvalidOperationException($"failed to update job result with taskId {taskId} and machineId+metricType {taskIdMachineIdMetric}");
                }
                break;
        }


        return true;

    }

    public async Async.Task<OneFuzzResultVoid> CreateOrUpdate(Guid jobId, Guid taskId, Guid machineId, DateTime createdAt, string resultType, Dictionary<string, double> resultValue) {

        var job = await _context.JobOperations.Get(jobId);
        if (job == null) {
            return OneFuzzResultVoid.Error(ErrorCode.INVALID_REQUEST, "invalid job");
        }

        var success = false;
        try {
            _logTracer.LogInformation("attempt to update job result {JobId}", job.JobId);
            var policy = Policy.Handle<InvalidOperationException>().WaitAndRetryAsync(50, _ => new TimeSpan(0, 0, 5));
            await policy.ExecuteAsync(async () => {
                success = await TryUpdate(job, taskId, machineId, createdAt, resultType, resultValue);
                _logTracer.LogInformation("attempt {success}", success);
            });
            return OneFuzzResultVoid.Ok;
        } catch (Exception e) {
            return OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_UPDATE, new string[] {
                    $"Unexpected failure when attempting to update job result for {job.JobId}",
                    $"Exception: {e}"
                });
        }
    }
}

