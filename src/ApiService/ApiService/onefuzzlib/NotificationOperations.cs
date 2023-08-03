using System.Text.Json;
using ApiService.OneFuzzLib.Orm;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;

public interface INotificationOperations : IOrm<Notification> {
    Async.Task<OneFuzzResultVoid> NewFiles(Container container, string filename, bool isLastRetryAttempt);
    IAsyncEnumerable<Notification> GetNotifications(Container container);
    IAsyncEnumerable<(Task, IEnumerable<Container>)> GetQueueTasks();
    Async.Task<OneFuzzResult<Notification>> Create(Container container, NotificationTemplate config, bool replaceExisting);
    Async.Task<Notification?> GetNotification(Guid notifificationId);

    System.Threading.Tasks.Task<OneFuzzResultVoid> TriggerNotification(Container container,
        Notification notification, IReport? reportOrRegression, bool isLastRetryAttempt = false);
}

public class NotificationOperations : Orm<Notification>, INotificationOperations {

    public NotificationOperations(ILogger<NotificationOperations> log, IOnefuzzContext context)
        : base(log, context) {

    }
    public async Async.Task<OneFuzzResultVoid> NewFiles(Container container, string filename, bool isLastRetryAttempt) {
        var result = OneFuzzResultVoid.Ok;

        // We don't want to store file added events for the events container because that causes an infinite loop
        if (container == WellKnownContainers.Events) {
            return result;
        }

        var notifications = GetNotifications(container);
        var hasNotifications = await notifications.AnyAsync();
        var reportOrRegression = await _context.Reports.GetReportOrRegression(container, filename, expectReports: hasNotifications);
        if (hasNotifications) {
            var done = new List<NotificationTemplate>();
            await foreach (var notification in notifications) {
                if (done.Contains(notification.Config)) {
                    continue;
                }

                done.Add(notification.Config);
                var notificationResult = await TriggerNotification(container, notification, reportOrRegression, isLastRetryAttempt);
                if (result.IsOk && !notificationResult.IsOk) {
                    result = notificationResult;
                }
            }
        }

        await foreach (var (task, containers) in GetQueueTasks()) {
            if (containers.Contains(container)) {
                _logTracer.LogInformation("queuing input {Container} {Filename} {TaskId}", container, filename, task.TaskId);

                var url = await _context.Containers.GetFileSasUrl(
                    container,
                    filename,
                    StorageType.Corpus,
                    BlobSasPermissions.Read | BlobSasPermissions.Delete);

                if (url is null) {
                    _logTracer.LogError("unable to generate sas url for missing container '{Container}'", container);
                    // try again later
                    throw new InvalidOperationException($"container '{container}' is missing");
                }

                await _context.Queue.SendMessage(task.TaskId.ToString(), url.ToString(), StorageType.Corpus);
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

        return result;
    }

    public async System.Threading.Tasks.Task<OneFuzzResultVoid> TriggerNotification(Container container,
        Notification notification, IReport? reportOrRegression, bool isLastRetryAttempt = false) {
        switch (notification.Config) {
            case TeamsTemplate teamsTemplate:
                await _context.Teams.NotifyTeams(teamsTemplate, container, reportOrRegression!,
                    notification.NotificationId);
                break;
            case AdoTemplate adoTemplate when reportOrRegression is not null:
                if (await _context.FeatureManagerSnapshot.IsEnabledAsync(FeatureFlagConstants.EnableWorkItemCreation)) {
                    return await _context.Ado.NotifyAdo(adoTemplate, container, reportOrRegression, isLastRetryAttempt,
                        notification.NotificationId);
                } else {
                    return OneFuzzResultVoid.Error(ErrorCode.ADO_WORKITEM_PROCESSING_DISABLED, "Work item processing is currently disabled");
                }
            case GithubIssuesTemplate githubIssuesTemplate when reportOrRegression is not null:
                await _context.GithubIssues.GithubIssue(githubIssuesTemplate, container, reportOrRegression,
                    notification.NotificationId);
                break;
        }

        return OneFuzzResultVoid.Ok;
    }

    public IAsyncEnumerable<Notification> GetNotifications(Container container) {
        return SearchByRowKeys(new[] { container.String });
    }

    public IAsyncEnumerable<(Task, IEnumerable<Container>)> GetQueueTasks() {
        // Nullability mismatch: We filter tuples where the containers are null
        return _context.TaskOperations.SearchStates(states: TaskStateHelper.AvailableStates)
            .Select(task => (task, containers: _context.TaskOperations.GetInputContainerQueues(task.Config)))
            .Where(x => x.containers.IsOk && x.containers.OkV is not null)
            .Select(x => (Task: x.task, Containers: x.containers.OkV!));
    }

    public async Async.Task<OneFuzzResult<Notification>> Create(Container container, NotificationTemplate config, bool replaceExisting) {
        if (await _context.Containers.FindContainer(container, StorageType.Corpus) == null) {
            return OneFuzzResult<Notification>.Error(ErrorCode.INVALID_REQUEST, "invalid container");
        }

        if (await _context.FeatureManagerSnapshot.IsEnabledAsync(FeatureFlagConstants.RenderOnlyScribanTemplates) &&
            !await JinjaTemplateAdapter.IsValidScribanNotificationTemplate(_context, _logTracer, config)) {
            return OneFuzzResult<Notification>.Error(ErrorCode.INVALID_REQUEST, "The notification config is not a valid scriban template");
        }

        if (await _context.FeatureManagerSnapshot.IsEnabledAsync(FeatureFlagConstants.SemanticNotificationConfigValidation)) {
            var validConfig = await config.Validate();
            if (!validConfig.IsOk) {
                return OneFuzzResult<Notification>.Error(validConfig.ErrorV);
            }
        }

        if (replaceExisting) {
            var existing = this.SearchByRowKeys(new[] { container.String });
            await foreach (var existingEntry in existing) {
                _logTracer.LogInformation("deleting existing notification: {NotificationId} - {Container}", existingEntry.NotificationId, container);
                var rr = await this.Delete(existingEntry);
                if (!rr.IsOk) {
                    _logTracer.AddHttpStatus(rr.ErrorV);
                    _logTracer.LogError("failed to delete existing notification {NotificationId} - {Container}", existingEntry.NotificationId, container);
                }
            }
        }
        var configWithHiddenSecret = await HideSecrets(config);
        var entry = new Notification(Guid.NewGuid(), container, configWithHiddenSecret);
        var r = await this.Insert(entry);
        if (!r.IsOk) {
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("failed to insert notification {NotificationId}", entry.NotificationId);
        }
        _logTracer.LogInformation("created notification {NotificationId} - {Container}", entry.NotificationId, entry.Container);

        return OneFuzzResult<Notification>.Ok(entry);
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
