using ApiService.OneFuzzLib.Orm;
using Microsoft.Azure.Functions.Worker;
namespace Microsoft.OneFuzz.Service.Functions;

public class TimerRetention {
    private readonly TimeSpan RETENTION_POLICY = TimeSpan.FromDays(18 * 30);
    private readonly TimeSpan SEARCH_EXTENT = TimeSpan.FromDays(20 * 30);

    private readonly ILogTracer _log;
    private readonly ITaskOperations _taskOps;
    private readonly INotificationOperations _notificaitonOps;
    private readonly IJobOperations _jobOps;
    private readonly IReproOperations _reproOps;
    private readonly IQueue _queue;
    private readonly IPoolOperations _poolOps;

    public TimerRetention(
            ILogTracer log,
            ITaskOperations taskOps,
            INotificationOperations notificaitonOps,
            IJobOperations jobOps,
            IReproOperations reproOps,
            IQueue queue,
            IPoolOperations poolOps) {
        _log = log;
        _taskOps = taskOps;
        _notificaitonOps = notificaitonOps;
        _jobOps = jobOps;
        _reproOps = reproOps;
        _queue = queue;
        _poolOps = poolOps;
    }


    [Function("TimerRetention")]
    public async Async.Task Run([TimerTrigger("20:00:00")] TimerInfo t) {
        var now = DateTimeOffset.UtcNow;

        var timeRetainedOlder = now - RETENTION_POLICY;
        var timeRetainedNewer = now + SEARCH_EXTENT;

        var timeFilter = Query.TimeRange(timeRetainedOlder, timeRetainedNewer);
        var timeFilterNewer = Query.TimestampNewerThan(timeRetainedOlder);

        // Collecting 'still relevant' task containers.
        // NOTE: This must be done before potentially modifying tasks otherwise
        // the task timestamps will not be useful.\

        var usedContainers = new HashSet<Container>();

        await foreach (var task in _taskOps.QueryAsync(timeFilter)) {
            var containerNames =
                from container in task.Config.Containers
                select container.Name;

            foreach (var c in containerNames) {
                _ = usedContainers.Add(c);
            }
        }

        await foreach (var notification in _notificaitonOps.QueryAsync(timeFilter)) {
            _log.Verbose($"checking expired notification for removal: {notification.NotificationId:Tag:NotificationId}");
            var container = notification.Container;

            if (!usedContainers.Contains(container)) {
                _log.Info($"deleting expired notification: {notification.NotificationId:Tag:NotificationId}");
                var r = await _notificaitonOps.Delete(notification);
                if (!r.IsOk) {
                    _log.WithHttpStatus(r.ErrorV).Error($"failed to delete notification {notification.NotificationId:Tag:NotificationId}");
                }
            }
        }

        await foreach (var job in _jobOps.QueryAsync(Query.And(timeFilter, Query.EqualEnum("state", JobState.Enabled)))) {
            if (job.UserInfo is not null && job.UserInfo.Upn is not null) {
                _log.Info($"removing PII from job {job.JobId:Tag:JobId}");
                var userInfo = job.UserInfo with { Upn = null };
                var updatedJob = job with { UserInfo = userInfo };
                var r = await _jobOps.Replace(updatedJob);
                if (!r.IsOk) {
                    _log.WithHttpStatus(r.ErrorV).Error($"Failed to save job {updatedJob.JobId:Tag:JobId}");
                }
            }
        }

        await foreach (var task in _taskOps.QueryAsync(Query.And(timeFilter, Query.EqualEnum("state", TaskState.Stopped)))) {
            if (task.UserInfo is not null && task.UserInfo.Upn is not null) {
                _log.Info($"removing PII from task {task.TaskId:Tag:TaskId}");
                var userInfo = task.UserInfo with { Upn = null };
                var updatedTask = task with { UserInfo = userInfo };
                var r = await _taskOps.Replace(updatedTask);
                if (!r.IsOk) {
                    _log.WithHttpStatus(r.ErrorV).Error($"Failed to save task {updatedTask.TaskId:Tag:TaskId}");
                }
            }
        }

        await foreach (var repro in _reproOps.QueryAsync(timeFilter)) {
            if (repro.UserInfo is not null && repro.UserInfo.Upn is not null) {
                _log.Info($"removing PII from repro: {repro.VmId:Tag:VmId}");
                var userInfo = repro.UserInfo with { Upn = null };
                var updatedRepro = repro with { UserInfo = userInfo };
                var r = await _reproOps.Replace(updatedRepro);
                if (!r.IsOk) {
                    _log.WithHttpStatus(r.ErrorV).Error($"Failed to save repro {updatedRepro.VmId:Tag:VmId}");
                }
            }
        }

        //delete Task queues for tasks that do not exist in the table (manually deleted from the table)
        //delete Pool queues for pools that were deleted before https://github.com/microsoft/onefuzz/issues/2430 got fixed
        await foreach (var q in _queue.ListQueues(StorageType.Corpus)) {
            Guid queueId;
            if (q.Name.StartsWith(IPoolOperations.PoolQueueNamePrefix)) {
                var queueIdStr = q.Name[IPoolOperations.PoolQueueNamePrefix.Length..];
                if (Guid.TryParse(queueIdStr, out queueId)) {
                    var pool = await _poolOps.GetById(queueId);
                    if (!pool.IsOk) {
                        //pool does not exist. Ok to delete the pool queue
                        _log.Info($"Deleting {q.Name:Tag:PoolQueueName} since pool could not be found in Pool table");
                        await _queue.DeleteQueue(q.Name, StorageType.Corpus);
                    }
                }
            } else if (Guid.TryParse(q.Name, out queueId)) {
                //this is a task queue
                var taskQueue = await _taskOps.GetByTaskId(queueId);
                if (taskQueue is null) {
                    // task does not exist. Ok to delete the task queue
                    _log.Info($"Deleting {q.Name:Tag:TaskQueueName} since task could not be found in Task table ");
                    await _queue.DeleteQueue(q.Name, StorageType.Corpus);
                }
            } else if (q.Name.StartsWith(ShrinkQueue.ShrinkQueueNamePrefix)) {
                //ignore Shrink Queues, since they seem to behave ok
            } else {
                _log.Warning($"Unhandled {q.Name:Tag:QueueName} when doing garbage collection on queues");
            }
        }
    }
}
