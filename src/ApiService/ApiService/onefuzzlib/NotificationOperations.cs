using System.Text.Json;
using ApiService.OneFuzzLib.Orm;
using Azure.Storage.Sas;

namespace Microsoft.OneFuzz.Service;

public interface INotificationOperations : IOrm<Notification> {
    Async.Task NewFiles(Container container, string filename, bool failTaskOnTransientError);
    IAsyncEnumerable<Notification> GetNotifications(Container container);
    IAsyncEnumerable<(Task, IEnumerable<string>)> GetQueueTasks();
}

public class NotificationOperations : Orm<Notification>, INotificationOperations {

    public NotificationOperations(ILogTracer log, IOnefuzzContext context)
        : base(log, context) {

    }
    public async Async.Task NewFiles(Container container, string filename, bool failTaskOnTransientError) {
        var notifications = GetNotifications(container);
        var hasNotifications = await notifications.AnyAsync();
        var reportOrRegression = await _context.Reports.GetReportOrRegression(container, filename, expectReports: hasNotifications);

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
                NotifyTeams(notification.Config.TeamsTemplate, container, filename, reportOrRegression);
            }

            if (reportOrRegression == null) {
                continue;
            }

            if (notification.Config.AdoTemplate != null) {
                NotifyAdo(notification.Config.AdoTemplate, container, filename, reportOrRegression, failTaskOnTransientError);
            }

            if (notification.Config.GithubIssuesTemplate != null) {
                GithubIssue(notification.Config.GithubIssuesTemplate, container, filename, reportOrRegression);
            }
        }

        await foreach (var (task, containers) in GetQueueTasks()) {
            if (containers.Contains(container.ContainerName)) {
                _logTracer.Info($"queuing input {container.ContainerName} {filename} {task.TaskId}");
                var url = _context.Containers.GetFileSasUrl(container, filename, StorageType.Corpus, BlobSasPermissions.Read | BlobSasPermissions.Delete);
                await _context.Queue.SendMessage(task.TaskId.ToString(), url?.ToString() ?? "", StorageType.Corpus);
            }
        }

        if (reportOrRegression is Report) {
            var report = (reportOrRegression as Report)!;
            var reportTask = await _context.TaskOperations.GetByJobIdAndTaskId(report.JobId, report.TaskId);

            var crashReportedEvent = new EventCrashReported(report, container, filename, reportTask?.Config);
            await _context.Events.SendEvent(crashReportedEvent);
        } else if (reportOrRegression is RegressionReport) {
            var regressionReport = (reportOrRegression as RegressionReport)!;
            var reportTask = await GetRegressionReportTask(regressionReport);

            var regressionEvent = new EventRegressionReported(regressionReport, container, filename, reportTask?.Config);
            await _context.Events.SendEvent(regressionEvent);
        } else {
            await _context.Events.SendEvent(new EventFileAdded(container, filename));
        }
    }

    public IAsyncEnumerable<Notification> GetNotifications(Container container) {
        return QueryAsync(filter: $"container eq '{container.ContainerName}'");
    }

    public IAsyncEnumerable<(Task, IEnumerable<string>)> GetQueueTasks() {
        // Nullability mismatch: We filter tuples where the containers are null
        return _context.TaskOperations.SearchStates(states: TaskStateHelper.AvailableStates)
            .Select(task => (task, _context.TaskOperations.GetInputContainerQueues(task.Config)))
            .Where(taskTuple => taskTuple.Item2 != null)!;
    }

    public async Async.Task<Task?> GetRegressionReportTask(RegressionReport report) {
        if (report.CrashTestResult.CrashReport != null) {
            return await _context.TaskOperations.GetByJobIdAndTaskId(report.CrashTestResult.CrashReport.JobId, report.CrashTestResult.CrashReport.TaskId);
        }
        if (report.CrashTestResult.NoReproReport != null) {
            return await _context.TaskOperations.GetByJobIdAndTaskId(report.CrashTestResult.NoReproReport.JobId, report.CrashTestResult.NoReproReport.TaskId);
        }

        _logTracer.Error($"unable to find crash_report or no repro entry for report: {JsonSerializer.Serialize(report)}");
        return null;
    }

    private void GithubIssue(GithubIssuesTemplate config, Container container, string filename, IReport? report) {
        throw new NotImplementedException();
    }

    private void NotifyAdo(AdoTemplate config, Container container, string filename, IReport report, bool failTaskOnTransientError) {
        throw new NotImplementedException();
    }

    private void NotifyTeams(TeamsTemplate config, Container container, string filename, IReport? report) {
        throw new NotImplementedException();
    }
}
