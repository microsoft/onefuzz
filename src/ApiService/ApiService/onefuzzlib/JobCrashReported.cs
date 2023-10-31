using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;

public interface IJobCrashReportedOperations : IOrm<JobCrashReported> {
    public Task<bool> CrashReported(Guid jobId);
    public Task<OneFuzzResultVoid> ReportCrash(Guid jobId, Guid taskId);
}

public class JobCrashReportedOperations : Orm<JobCrashReported>, IJobCrashReportedOperations {
    public JobCrashReportedOperations(ILogger<JobCrashReportedOperations> logTracer, IOnefuzzContext context) : base(logTracer, context) {
    }

    public async Task<bool> CrashReported(Guid jobId) {
        return await QueryAsync(Query.PartitionKey(jobId.ToString())).AnyAsync();
    }

    public async Task<OneFuzzResultVoid> ReportCrash(Guid jobId, Guid taskId) {

        var result = await Update(new JobCrashReported(jobId, taskId));
        if (!result.IsOk) {
            return OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_UPDATE, "Failed to update job crash reported");
        }

        return OneFuzzResultVoid.Ok;
    }
}
