﻿using System.Text.Json;
using ApiService.OneFuzzLib.Orm;
using Azure.Storage.Sas;

namespace Microsoft.OneFuzz.Service;

public interface INotificationOperations : IOrm<Notification> {
    Async.Task NewFiles(Container container, string filename, bool isLastRetryAttempt);
    IAsyncEnumerable<Notification> GetNotifications(Container container);
    IAsyncEnumerable<(Task, IEnumerable<Container>)> GetQueueTasks();
    Async.Task<OneFuzzResult<Notification>> Create(Container container, NotificationTemplate config, bool replaceExisting);
    Async.Task<Notification?> GetNotification(Guid notifificationId);

    System.Threading.Tasks.Task<OneFuzzResultVoid> TriggerNotification(Container container,
        Notification notification, IReport? reportOrRegression, bool isLastRetryAttempt = false);
}

public class NotificationOperations : Orm<Notification>, INotificationOperations {

    public NotificationOperations(ILogTracer log, IOnefuzzContext context)
        : base(log, context) {

    }
    public async Async.Task NewFiles(Container container, string filename, bool isLastRetryAttempt) {
        // We don't want to store file added events for the events container because that causes an infinite loop
        if (container == WellKnownContainers.Events) {
            return;
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
                _ = await TriggerNotification(container, notification, reportOrRegression, isLastRetryAttempt);
            }
        }

        await foreach (var (task, containers) in GetQueueTasks()) {
            if (containers.Contains(container)) {
                _logTracer.Info($"queuing input {container} {filename} {task.TaskId}");
                var url = await _context.Containers.GetFileSasUrl(container, filename, StorageType.Corpus, BlobSasPermissions.Read | BlobSasPermissions.Delete);
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
    }

    public async System.Threading.Tasks.Task<OneFuzzResultVoid> TriggerNotification(Container container,
        Notification notification, IReport? reportOrRegression, bool isLastRetryAttempt = false) {
        switch (notification.Config) {
            case TeamsTemplate teamsTemplate:
                await _context.Teams.NotifyTeams(teamsTemplate, container, reportOrRegression!,
                    notification.NotificationId);
                break;
            case AdoTemplate adoTemplate when reportOrRegression is not null:
                return await _context.Ado.NotifyAdo(adoTemplate, container, reportOrRegression, isLastRetryAttempt,
                    notification.NotificationId);
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
            .Select(task => (task, _context.TaskOperations.GetInputContainerQueues(task.Config)))
            .Where(taskTuple => taskTuple.Item2.IsOk && taskTuple.Item2.OkV != null)
            .Select(x => (Task: x.Item1, Containers: x.Item2.OkV))!;
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
                _logTracer.Info($"deleting existing notification: {existingEntry.NotificationId:Tag:NotificationId} - {container:Tag:Container}");
                var rr = await this.Delete(existingEntry);
                if (!rr.IsOk) {
                    _logTracer.WithHttpStatus(rr.ErrorV).Error($"failed to delete existing notification {existingEntry.NotificationId:Tag:NotificationId} - {container:Tag:Container}");
                }
            }
        }
        var configWithHiddenSecret = await HideSecrets(config);
        var entry = new Notification(Guid.NewGuid(), container, configWithHiddenSecret);
        var r = await this.Insert(entry);
        if (!r.IsOk) {
            _logTracer.WithHttpStatus(r.ErrorV).Error($"failed to insert notification {entry.NotificationId:Tag:NotificationId}");
        }
        _logTracer.Info($"created notification {entry.NotificationId:Tag:NotificationId} - {entry.Container:Tag:Container}");

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

        _logTracer.Error($"unable to find crash_report or no repro entry for report: {JsonSerializer.Serialize(report)}");
        return null;
    }

    public async Async.Task<Notification?> GetNotification(Guid notifificationId) {
        return await SearchByPartitionKeys(new[] { notifificationId.ToString() }).SingleOrDefaultAsync();
    }
}
