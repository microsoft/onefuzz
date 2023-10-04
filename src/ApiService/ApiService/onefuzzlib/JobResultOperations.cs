using ApiService.OneFuzzLib.Orm;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;

public interface IJobResultOperations : IOrm<JobResult> {

    Async.Task<JobResult?> GetJobResult(Guid jobId);
    Async.Task<OneFuzzResultVoid> CreateOrUpdate(Guid jobId, Guid taskId, Guid machineId, JobResultType resultType, Dictionary<string, double> resultValue);

}
public class JobResultOperations : Orm<JobResult>, IJobResultOperations {

    public JobResultOperations(ILogger<JobResultOperations> log, IOnefuzzContext context)
        : base(log, context) {
    }

    public async Async.Task<JobResult?> GetJobResult(Guid jobId) {
        return await SearchByPartitionKeys(new[] { jobId.ToString() }).SingleOrDefaultAsync();
    }

    // // private JobResult UpdateResult(JobResult result, Guid machineId, JobResultType type, Dictionary<string, double> resultValue) {

    // //     var newResult = result;
    // //     double newValue;
    // //     switch (type) {
    // //         case JobResultType.NewCrashingInput:
    // //             newValue = result.NewCrashingInput + resultValue["count"];
    // //             newResult = result with { NewCrashingInput = newValue };
    // //             break;
    // //         case JobResultType.NewReport:
    // //             newValue = result.NewReport + resultValue["count"];
    // //             newResult = result with { NewReport = newValue };
    // //             break;
    // //         case JobResultType.NewUniqueReport:
    // //             newValue = result.NewUniqueReport + resultValue["count"];
    // //             newResult = result with { NewUniqueReport = newValue };
    // //             break;
    // //         case JobResultType.NewRegressionReport:
    // //             newValue = result.NewRegressionReport + resultValue["count"];
    // //             newResult = result with { NewRegressionReport = newValue };
    // //             break;
    // //         case JobResultType.NewCrashDump:
    // //             newValue = result.NewCrashDump + resultValue["count"];
    // //             newResult = result with { NewCrashDump = newValue };
    // //             break;
    // //         case JobResultType.NewUnableToReproduce:
    // //             newValue = result.NewUnableToReproduce + resultValue["count"];
    // //             newResult = result with { NewUnableToReproduce = newValue };
    // //             break;
    // //         case JobResultType.CoverageData:
    // //             double newCovered = resultValue["covered"];
    // //             double newTotalCovered = resultValue["features"];
    // //             double newCoverageRate = resultValue["rate"];
    // //             newResult = result with { InstructionsCovered = newCovered, TotalInstructions = newTotalCovered, CoverageRate = newCoverageRate };
    // //             break;
    // //         case JobResultType.RuntimeStats:
    // //             double newTotalIterations = resultValue["total_count"];
    // //             Dictionary<Guid, double>? resultDictionary = result.IterationDictionary;
    // //             if (resultDictionary == null) {
    // //                 resultDictionary = new Dictionary<Guid, double>() {
    // //                     { machineId, newTotalIterations }
    // //                 };
    // //             } else {
    // //                 resultDictionary[machineId] = newTotalIterations;
    // //             }

    // //             newResult = result with { IterationDictionary = resultDictionary };
    // //             break;
    // //         default:
    // //             _logTracer.LogWarning($"Invalid Field {type}.");
    // //             break;
    // //     }
    // //     _logTracer.LogInformation($"Attempting to log result: {newResult}");
    // //     return newResult;
    // }

    private async Async.Task<bool> InsertEntry(Job job, Guid taskId, Guid machineId, JobResultType resultType, Dictionary<string, double> resultValue) {
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

    public async Async.Task<OneFuzzResultVoid> CreateOrUpdate(Guid jobId, Guid taskId, Guid machineId, JobResultType resultType, Dictionary<string, double> resultValue) {

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


        // var success = false;
        // try {
        //     _logTracer.LogInformation("attempt to update job result {JobId}", job.JobId);
        //     var policy = Policy.Handle<InvalidOperationException>().WaitAndRetryAsync(50, _ => new TimeSpan(0, 0, 5));
        //     await policy.ExecuteAsync(async () => {
        //         success = await TryUpdate(job, machineId, resultType, resultValue);
        //         _logTracer.LogInformation("attempt {success}", success);
        //     });
        //     return OneFuzzResultVoid.Ok;
        // } catch (Exception e) {
        //     return OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_UPDATE, new string[] {
        //             $"Unexpected failure when attempting to update job result for {job.JobId}",
        //             $"Exception: {e}"
        //         });
        // }
    }
}

