using System.Text.Json;
using ApiService.OneFuzzLib.Orm;
using Azure.Storage.Sas;

namespace Microsoft.OneFuzz.Service;

public interface INotificationOperations : IOrm<Notification> {
    Async.Task NewFiles(Container container, string filename, bool failTaskOnTransientError);
}

public class NotificationOperations : Orm<Notification>, INotificationOperations {

    public NotificationOperations(ILogTracer log, IOnefuzzContext context)
        : base(log, context) {

    }
    public async Async.Task NewFiles(Container container, string filename, bool failTaskOnTransientError) {
        var notifications = GetNotifications(container);
        var hasNotifications = await notifications.AnyAsync();
        var report = await _context.Reports.GetReportOrRegression(container, filename, expectReports: hasNotifications);

        if (!hasNotifications) {
            return;
        }

        var done = new List<NotificationTemplate>();
        await foreach (var notification in notifications) {
            if (done.Contains(notification.Config)) {
                continue;
            }

            done.Add(notification.Config);

            if (notification.Config.TeamsTemplate != null) {
                NotifyTeams(notification.Config.TeamsTemplate, container, filename, report);
            }

            if (report == null) {
                continue;
            }

            if (notification.Config.AdoTemplate != null) {
                NotifyAdo(notification.Config.AdoTemplate, container, filename, report, failTaskOnTransientError);
            }

            if (notification.Config.GithubIssuesTemplate != null) {
                GithubIssue(notification.Config.GithubIssuesTemplate, container, filename, report);
            }
        }

        await foreach (var (task, containers) in GetQueueTasks()) {
            if (containers.Contains(container.ContainerName)) {
                _logTracer.Info($"queuing input {container.ContainerName} {filename} {task.TaskId}");
                var url = _context.Containers.GetFileSasUrl(container, filename, StorageType.Corpus, BlobSasPermissions.Read | BlobSasPermissions.Delete);
                await _context.Queue.SendMessage(task.TaskId.ToString(), url?.ToString() ?? "", StorageType.Corpus);
            }
        }

        if (report == null) {
            await _context.Events.SendEvent(new EventFileAdded(container, filename));
        } else if (report.Report != null) {
            var reportTask = await _context.TaskOperations.GetByJobIdAndTaskId(report.Report.JobId, report.Report.TaskId);

            var crashReportedEvent = new EventCrashReported(report.Report, container, filename, reportTask?.Config);
            await _context.Events.SendEvent(crashReportedEvent);
        } else if (report.RegressionReport != null) {
            var reportTask = await GetRegressionReportTask(report.RegressionReport);

            var regressionEvent = new EventRegressionReported(report.RegressionReport, container, filename, reportTask?.Config);
        }
    }

    public IAsyncEnumerable<Notification> GetNotifications(Container container) {
        return QueryAsync(filter: $"container eq '{container.ContainerName}'");
    }

    public IAsyncEnumerable<(Task, IEnumerable<string>)> GetQueueTasks() {
        // Nullability mismatch: We filter tuples where the containers are null
        return _context.TaskOperations.SearchStates(states: TaskStateHelper.Available)
            .Select(task => (task, _context.TaskOperations.GetInputContainerQueues(task.Config)))
            .Where(taskTuple => taskTuple.Item2 != null)!;
    }

    private async Async.Task<Task?> GetRegressionReportTask(RegressionReport report) {
        if (report.CrashTestResult.CrashReport != null) {
            return await _context.TaskOperations.GetByJobIdAndTaskId(report.CrashTestResult.CrashReport.JobId, report.CrashTestResult.CrashReport.TaskId);
        }
        if (report.CrashTestResult.NoReproReport != null) {
            return await _context.TaskOperations.GetByJobIdAndTaskId(report.CrashTestResult.NoReproReport.JobId, report.CrashTestResult.NoReproReport.TaskId);
        }

        _logTracer.Error($"unable to find crash_report or no repro entry for report: {JsonSerializer.Serialize(report)}");
        return null;
    }

    private void GithubIssue(GithubIssuesTemplate config, Container container, string filename, RegressionReportOrReport? report) {
        throw new NotImplementedException();
    }

    private void NotifyAdo(AdoTemplate config, Container container, string filename, RegressionReportOrReport report, bool failTaskOnTransientError) {
        throw new NotImplementedException();
    }

    private void NotifyTeams(TeamsTemplate config, Container container, string filename, RegressionReportOrReport? report) {
        throw new NotImplementedException();
    }
}
