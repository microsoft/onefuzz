using ApiService.OneFuzzLib.Orm;
using Microsoft.Extensions.Logging;
using Polly;
namespace Microsoft.OneFuzz.Service;

public interface IJobResultOperations : IOrm<JobResult> {

    Async.Task<JobResult?> GetJobResult(Guid taskId, string machineIdMetric);
    Async.Task<OneFuzzResultVoid> CreateOrUpdate(Guid jobId, Guid taskId, Guid machineId, string resultType, Dictionary<string, double> resultValue);

}
public class JobResultOperations : Orm<JobResult>, IJobResultOperations {

    public JobResultOperations(ILogger<JobResultOperations> log, IOnefuzzContext context)
        : base(log, context) {
    }

    public async Async.Task<JobResult?> GetJobResult(Guid taskId, string machineIdMetric) {
        var data = QueryAsync(Query.SingleEntity(taskId.ToString(), machineIdMetric));
        return await data.FirstOrDefaultAsync();
    }

    private async Async.Task<bool> TryUpdate(Job job, Guid taskId, Guid machineId, string resultType, Dictionary<string, double> resultValue) {
        var jobId = job.JobId;
        var machineIdMetric = string.Concat(machineId, resultType);

        Dictionary<string, double> newResultValue;
        var jobResult = await GetJobResult(taskId, machineIdMetric);

        if (jobResult == null) {
            _logTracer.LogInformation($"attempt to insert new job result {taskId} and machineId+metricType {machineIdMetric}");
            var entry = new JobResult(TaskId: taskId, MachineIdMetric: machineIdMetric, JobId: jobId, Project: job.Config.Project, Name: job.Config.Name, resultType, resultValue);
            var r = await Insert(entry);
            if (!r.IsOk) {
                throw new InvalidOperationException($"failed to insert job result with taskId {taskId} and machineId+metricType {machineIdMetric}");
            }
        } else if (resultType.Equals("CoverageData") && jobResult.MetricValue["rate"] < resultValue["rate"]) {
            _logTracer.LogInformation($"attempt to replace coverage job result for {taskId} and machineId+metricType {machineIdMetric}");
            var entry = new JobResult(TaskId: taskId, MachineIdMetric: machineIdMetric, JobId: jobId, Project: job.Config.Project, Name: job.Config.Name, resultType, resultValue);
            var r = await Replace(entry);
            if (!r.IsOk) {
                throw new InvalidOperationException($"failed to insert job result with taskId {taskId} and machineId+metricType {machineIdMetric}");
            }
        } else if (resultType.Equals("RuntimeStats") && jobResult.MetricValue["total_count"] < resultValue["total_count"]) {
            _logTracer.LogInformation($"attempt to replace runtime stats job result for {taskId} and machineId+metricType {machineIdMetric}");
            var entry = new JobResult(TaskId: taskId, MachineIdMetric: machineIdMetric, JobId: jobId, Project: job.Config.Project, Name: job.Config.Name, resultType, resultValue);
            var r = await Replace(entry);
            if (!r.IsOk) {
                throw new InvalidOperationException($"failed to insert job result with taskId {taskId} and machineId+metricType {machineIdMetric}");
            }
        } else {
            jobResult.MetricValue["count"]++;
            newResultValue = jobResult.MetricValue;
            // var entry = new JobResult(TaskId: taskId, MachineIdMetric: machineIdMetric, JobId: jobId, Project: job.Config.Project, Name: job.Config.Name, resultType, newResultValue);
            var newResult = jobResult with { MetricValue = newResultValue };
            _logTracer.LogInformation($"attempt to update job result {taskId} and machineId+metricType {machineIdMetric} with new count: {newResultValue}");
            var r = await Update(newResult);
            if (!r.IsOk) {
                throw new InvalidOperationException($"failed to update job result with taskId {taskId} and machineId+metricType {machineIdMetric}");
            }
        }

        _logTracer.LogInformation($"created job result with taskId {taskId} and machineId+metricType {machineIdMetric}");

        return true;
    }

    public async Async.Task<OneFuzzResultVoid> CreateOrUpdate(Guid jobId, Guid taskId, Guid machineId, string resultType, Dictionary<string, double> resultValue) {

        var job = await _context.JobOperations.Get(jobId);
        if (job == null) {
            return OneFuzzResultVoid.Error(ErrorCode.INVALID_REQUEST, "invalid job");
        }

        var success = false;
        try {
            _logTracer.LogInformation("attempt to update job result {JobId}", job.JobId);
            var policy = Policy.Handle<InvalidOperationException>().WaitAndRetryAsync(50, _ => new TimeSpan(0, 0, 5));
            await policy.ExecuteAsync(async () => {
                success = await TryUpdate(job, taskId, machineId, resultType, resultValue);
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

