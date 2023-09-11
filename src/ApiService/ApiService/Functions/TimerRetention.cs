using ApiService.OneFuzzLib.Orm;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Microsoft.OneFuzz.Service.Functions;

public class TimerRetention {
    private readonly TimeSpan RETENTION_POLICY = TimeSpan.FromDays(18 * 30);
    private readonly TimeSpan SEARCH_EXTENT = TimeSpan.FromDays(20 * 30);

    private readonly IOnefuzzContext _context;
    private readonly ILogger _log;
    private readonly ITaskOperations _taskOps;
    private readonly INotificationOperations _notificationOps;
    private readonly IJobOperations _jobOps;
    private readonly IQueue _queue;
    private readonly IPoolOperations _poolOps;

    public TimerRetention(
            ILogger<TimerRetention> log,
            IOnefuzzContext context) {
        _context = context;
        _log = log;
        _taskOps = _context.TaskOperations;
        _notificationOps = _context.NotificationOperations;
        _jobOps = _context.JobOperations;
        _queue = _context.Queue;
        _poolOps = _context.PoolOperations;
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

        await foreach (var notification in _notificationOps.QueryAsync(timeFilter)) {
            _log.LogDebug("checking expired notification for removal: {NotificationId}", notification.NotificationId);
            var container = notification.Container;

            if (!usedContainers.Contains(container)) {
                _log.LogInformation("deleting expired notification: {NotificationId}", notification.NotificationId);
                var r = await _notificationOps.Delete(notification);
                if (!r.IsOk) {
                    _log.AddHttpStatus(r.ErrorV);
                    _log.LogError("failed to delete notification {NotificationId}", notification.NotificationId);
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
                        _log.LogInformation("Deleting {PoolQueueName} since pool could not be found in Pool table", q.Name);
                        await _queue.DeleteQueue(q.Name, StorageType.Corpus);
                    }
                }
            } else if (Guid.TryParse(q.Name, out queueId)) {
                //this is a task queue
                var taskQueue = await _taskOps.GetByTaskId(queueId);
                if (taskQueue is null) {
                    // task does not exist. Ok to delete the task queue
                    _log.LogInformation("Deleting {TaskQueueName} since task could not be found in Task table", q.Name);
                    await _queue.DeleteQueue(q.Name, StorageType.Corpus);
                }
            } else if (q.Name.StartsWith(ShrinkQueue.ShrinkQueueNamePrefix)) {
                //ignore Shrink Queues, since they seem to behave ok
            } else {
                _log.LogWarning("Unhandled {QueueName} when doing garbage collection on queues", q.Name);
            }
        }
    }

    // 0 0 5 * * * - Once a day, every day, at 5AM
    [Function("TimerBlobRetention")]
    public async Async.Task TimerBlobRetention([TimerTrigger("0 0 5 * * *")] TimerInfo t) {
        if (await _context.FeatureManagerSnapshot.IsEnabledAsync(FeatureFlagConstants.EnableBlobRetentionPolicy)) {
            await _context.Containers.DeleteAllExpiredBlobs();
        }
    }
}
