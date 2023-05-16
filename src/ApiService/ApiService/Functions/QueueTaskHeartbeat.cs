using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service.Functions;


public class QueueTaskHearbeat {
    private readonly ILogTracer _log;
    private readonly IOnefuzzContext _context;

    public QueueTaskHearbeat(ILogTracer logTracer, IOnefuzzContext context) {
        _log = logTracer;
        _context = context;
    }

    [Function("QueueTaskHeartbeat")]
    public async Async.Task Run([QueueTrigger("task-heartbeat", Connection = "AzureWebJobsStorage")] string msg) {

        var _tasks = _context.TaskOperations;
        var _jobs = _context.JobOperations;
        var _events = _context.Events;
        var _metrics = _context.Metrics;

        _log.Info($"heartbeat: {msg}");
        var hb = JsonSerializer.Deserialize<TaskHeartbeatEntry>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}");

        var task = await _tasks.GetByTaskId(hb.TaskId);
        if (task == null) {
            _log.Warning($"invalid {hb.TaskId:Tag:TaskId}");
            return;
        }

        var job = await _jobs.Get(task.JobId);
        if (job == null) {
            _log.Warning($"invalid {task.JobId:Tag:JobId}");
            return;
        }
        var newTask = task with { Heartbeat = DateTimeOffset.UtcNow };
        var r = await _tasks.Replace(newTask);
        if (!r.IsOk) {
            _log.WithHttpStatus(r.ErrorV).Error($"failed to replace with new task {hb.TaskId:Tag:TaskId}");
        }

        var taskHeartBeatEvent = new EventTaskHeartbeat(newTask.JobId, newTask.TaskId, job.Config.Project, job.Config.Name, newTask.State, newTask.Config);
        await _events.SendEvent(taskHeartBeatEvent);
        if (await _context.FeatureManagerSnapshot.IsEnabledAsync(FeatureFlagConstants.EnableCustomMetricTelemetry)) {
            _metrics.SendMetric(1, taskHeartBeatEvent);
        }
    }
}
