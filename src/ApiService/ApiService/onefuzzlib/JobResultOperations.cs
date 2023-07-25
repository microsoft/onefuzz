using System.Text.Json;
using ApiService.OneFuzzLib.Orm;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;

public interface IJobResultOperations : IOrm<JobResult> {

    Async.Task<JobResult?> GetJobResult(Guid jobId);
    JobResult UpdateResult(JobResult result, ResultType type);
    Async.Task<OneFuzzResult<JobResult>> Create(Guid jobId, ResultType resultType, bool replaceExisting);

    public class JobResultOperations : Orm<JobResult>, IJobResultOperations {

        public JobResultOperations(ILogger<JobResultOperations> log, IOnefuzzContext context)
            : base(log, context) {
        }

        public async Async.Task<JobResult?> GetJobResult(Guid jobId) {
            return await SearchByPartitionKeys(new[] { jobId.ToString() }).SingleOrDefaultAsync();
        }

        public JobResult UpdateResult(JobResult result, ResultType type) {
            // return await SearchByPartitionKeys(new[] { jobId.ToString() }).SingleOrDefaultAsync();

            var newResult = result;
            int newValue;
            switch (type) {
                case ResultType.NewCrashingInput:
                    newValue = result.NewCrashingInput + 1;
                    newResult = result with { NewCrashingInput = newValue };
                    break;
                case ResultType.NewReport:
                    newValue = result.NewReport + 1;
                    newResult = result with { NewReport = newValue };
                    break;
                case ResultType.NewUniqueReport:
                    newValue = result.NewReport + 1;
                    newResult = result with { NewReport = newValue };
                    break;
                case ResultType.NewRegressionReport:
                    newValue = result.NewRegressionReport + 1;
                    newResult = result with { NewRegressionReport = newValue };
                    break;
                default:
                    _logTracer.LogInformation("Invalid Field.");
                    break;
            }

            return newResult;
        }

        public async Async.Task<OneFuzzResult<JobResult>> Create(Guid jobId, ResultType resultType, bool replaceExisting) {

            var job = await _context.JobOperations.Get(jobId);
            if (job == null) {
                return OneFuzzResult<JobResult>.Error(ErrorCode.INVALID_REQUEST, "invalid job");
            }

            var jobResult = await GetJobResult(jobId);

            if (jobResult == null) {
                _logTracer.LogInformation("Creating new JobResult for Job {JobId}", jobId);

                var entry = new JobResult(JobId: jobId, Project: job.Config.Project, Name: job.Config.Name, 0, 0, 0, 0, 0);

                jobResult = UpdateResult(entry, resultType);

                var r = await this.Insert(jobResult);
                if (!r.IsOk) {
                    _logTracer.AddHttpStatus(r.ErrorV);
                    _logTracer.LogError("failed to insert job result {JobId}", jobResult.JobId);
                }
                _logTracer.LogInformation("created job result {JobId}", jobResult.JobId);
            } else {
                _logTracer.LogInformation("Updating existing JobResult entry for Job {JobId}", jobId);

                jobResult = UpdateResult(jobResult, resultType);

                var r = await Replace(jobResult);
                if (!r.IsOk) {
                    _logTracer.AddHttpStatus(r.ErrorV);
                    _logTracer.LogError("failed to insert job result {JobId}", jobResult.JobId);
                }
                _logTracer.LogInformation("updated job result {JobId}", jobResult.JobId);
            }

            return OneFuzzResult<JobResult>.Ok(jobResult);
        }


        private async Async.Task<NotificationTemplate> HideSecrets(NotificationTemplate notificationTemplate) {

            switch (notificationTemplate) {
                case AdoTemplate adoTemplate:
                    var hiddenAuthToken = await _context.SecretsOperations.StoreSecretData(adoTemplate.AuthToken);
                    return adoTemplate with { AuthToken = hiddenAuthToken };
                case GithubIssuesTemplate githubIssuesTemplate:
                    var hiddenAuth = await _context.SecretsOperations.StoreSecretData(githubIssuesTemplate.Auth);
                    return githubIssuesTemplate with { Auth = hiddenAuth };
                case TeamsTemplate teamsTemplate:
                    var hiddenUrl = await _context.SecretsOperations.StoreSecretData(teamsTemplate.Url);
                    return teamsTemplate with { Url = hiddenUrl };
                default:
                    throw new ArgumentOutOfRangeException(nameof(notificationTemplate));
            }

        }

        public async Async.Task<Task?> GetRegressionReportTask(RegressionReport report) {
            if (report.CrashTestResult.CrashReport != null) {
                return await _context.TaskOperations.GetByJobIdAndTaskId(report.CrashTestResult.CrashReport.JobId, report.CrashTestResult.CrashReport.TaskId);
            }
            if (report.CrashTestResult.NoReproReport != null) {
                return await _context.TaskOperations.GetByJobIdAndTaskId(report.CrashTestResult.NoReproReport.JobId, report.CrashTestResult.NoReproReport.TaskId);
            }

            _logTracer.LogError("unable to find crash_report or no repro entry for report: {report}", JsonSerializer.Serialize(report));
            return null;
        }

        public async Async.Task<Notification?> GetNotification(Guid notifificationId) {
            return await SearchByPartitionKeys(new[] { notifificationId.ToString() }).SingleOrDefaultAsync();
        }
    }
