using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
namespace Microsoft.OneFuzz.Service.Functions;


public class QueueJobResult {
    private readonly ILogger _log;
    private readonly IOnefuzzContext _context;

    public QueueJobResult(ILogger<QueueJobResult> logTracer, IOnefuzzContext context) {
        _log = logTracer;
        _context = context;
    }

    [Function("QueueTaskJobResult")]
    public async Async.Task Run([QueueTrigger("task-heartbeat", Connection = "AzureWebJobsStorage")] string msg) {

        var _tasks = _context.TaskOperations;
        var _jobs = _context.JobOperations;
        var _events = _context.Events;
        var _metrics = _context.Metrics;

        _log.LogInformation("heartbeat: {msg}", msg);
        var hb = JsonSerializer.Deserialize<TaskJobResultEntry>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}");

        var task = await _tasks.GetByTaskId(hb.TaskId);
        if (task == null) {
            _log.LogWarning("invalid {TaskId}", hb.TaskId);
            return;
        }

        var job = await _jobs.Get(task.JobId);
        if (job == null) {
            _log.LogWarning("invalid {JobId}", task.JobId);
            return;
        }

        HeartbeatData? data;
        if (hb.Data.Length > 0)
            data = hb.Data[0];
        else {
            _log.LogWarning($"heartbeat data is empty, throwing out: {hb}");
            return;
        }

        var heartbeatType = data.Type;
        _log.LogInformation($"heartbeat data type: {heartbeatType}");

        switch (heartbeatType) {
            case HeartbeatType.TaskAlive:
                var newTask = task with { Heartbeat = DateTimeOffset.UtcNow };
                var r = await _tasks.Replace(newTask);
                if (!r.IsOk) {
                    _log.AddHttpStatus(r.ErrorV);
                    _log.LogError("failed to replace with new task {TaskId}", hb.TaskId);
                }

                var taskHeartBeatEvent = new EventTaskJobResult(newTask.JobId, newTask.TaskId, job.Config.Project, job.Config.Name, newTask.State, newTask.Config);
                await _events.SendEvent(taskHeartBeatEvent);
                if (await _context.FeatureManagerSnapshot.IsEnabledAsync(FeatureFlagConstants.EnableCustomMetricTelemetry)) {
                    _metrics.SendMetric(1, taskHeartBeatEvent);
                }
                break;
            case HeartbeatType.MachineAlive:
                _log.LogInformation($"machine alive heartbeat, skip: {heartbeatType}");
                break;
            default:
                var jobResult = await _context.JobResultOperations.CreateOrUpdate(job.JobId, heartbeatType);
                if (!jobResult.IsOk) {
                    _log.LogError("failed to create or update with job result {JobId}", job.JobId);
                }
                break;
        }
    }
}
