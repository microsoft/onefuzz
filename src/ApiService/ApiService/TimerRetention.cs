using Microsoft.Azure.Functions.Worker;

namespace Microsoft.OneFuzz.Service;

public class TimerRetention {
    private readonly TimeSpan RETENTION_POLICY = TimeSpan.FromDays(18 * 30);
    private readonly TimeSpan SEARCH_EXTENT = TimeSpan.FromDays(20 * 30);

    private readonly ILogTracer _log;
    private readonly ITaskOperations _taskOps;
    private readonly INotificationOperations _notificaitonOps;
    private readonly IJobOperations _jobOps;
    private readonly IReproOperations _reproOps;

    public TimerRetention(
            ILogTracer log,
            ITaskOperations taskOps,
            INotificationOperations notificaitonOps,
            IJobOperations jobOps,
            IReproOperations reproOps) {
        _log = log;
        _taskOps = taskOps;
        _notificaitonOps = notificaitonOps;
        _jobOps = jobOps;
        _reproOps = reproOps;
    }


    public async Async.Task Run([TimerTrigger("20:00:00")] TimerInfo t) {
        var now = DateTimeOffset.UtcNow;

        var timeRetainedOlder = now - RETENTION_POLICY;
        var timeRetainedNewer = now + SEARCH_EXTENT;

        var timeFilter = $"Timestamp lt datetime'{timeRetainedOlder.ToString("o")}' and Timestamp gt datetime'{timeRetainedNewer.ToString("o")}'";
        var timeFilterNewer = $"Timestamp gt datetime '{timeRetainedOlder.ToString("o")}'";

        // Collecting 'still relevant' task containers.
        // NOTE: This must be done before potentially modifying tasks otherwise
        // the task timestamps will not be useful.\

        var usedContainers = new HashSet<Container>();

        await foreach (var task in _taskOps.QueryAsync(timeFilter)) {
            var containerNames =
                from container in task.Config.Containers
                select container.Name;

            foreach (var c in containerNames) {
                usedContainers.Add(c);
            }
        }


        await foreach (var notification in _notificaitonOps.QueryAsync(timeFilter)) {
            _log.Verbose($"checking expired notification for removal: {notification.NotificationId}");
            var container = notification.Container;

            if (!usedContainers.Contains(container)) {
                _log.Info($"deleting expired notification: {notification.NotificationId}");
                var r = await _notificaitonOps.Delete(notification);
                if (!r.IsOk) {
                    _log.Error($"failed to delete notification with id {notification.NotificationId} due to [{r.ErrorV.Item1}] {r.ErrorV.Item2}");
                }
            }
        }

        await foreach (var job in _jobOps.QueryAsync($"{timeFilter} and state eq '{JobState.Enabled}'")) {
            if (job.UserInfo is not null && job.UserInfo.Upn is not null) {
                _log.Info($"removing PII from job {job.JobId}");
                var userInfo = job.UserInfo with { Upn = null };
                var updatedJob = job with { UserInfo = userInfo };
                var r = await _jobOps.Replace(updatedJob);
                if (!r.IsOk) {
                    _log.Error($"Failed to save job {updatedJob.JobId} due to [{r.ErrorV.Item1}] {r.ErrorV.Item2}");
                }
            }
        }

        await foreach (var task in _taskOps.QueryAsync($"{timeFilter} and state eq '{TaskState.Stopped}'")) {
            if (task.UserInfo is not null && task.UserInfo.Upn is not null) {
                _log.Info($"removing PII from task {task.TaskId}");
                var userInfo = task.UserInfo with { Upn = null };
                var updatedTask = task with { UserInfo = userInfo };
                var r = await _taskOps.Replace(updatedTask);
                if (!r.IsOk) {
                    _log.Error($"Failed to save task {updatedTask.TaskId} due to [{r.ErrorV.Item1}] {r.ErrorV.Item2}");
                }
            }
        }

        await foreach (var repro in _reproOps.QueryAsync(timeFilter)) {
            if (repro.UserInfo is not null && repro.UserInfo.Upn is not null) {
                _log.Info($"removing PII from repro: {repro.VmId}");
                var userInfo = repro.UserInfo with { Upn = null };
                var updatedRepro = repro with { UserInfo = userInfo };
                var r = await _reproOps.Replace(updatedRepro);
                if (!r.IsOk) {
                    _log.Error($"Failed to save repro {updatedRepro.VmId} due to [{r.ErrorV.Item1}] {r.ErrorV.Item2}");
                }
            }
        }
    }
}
