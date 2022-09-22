using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service.Functions;


public class QueueTaskHearbeat {
    private readonly ILogTracer _log;

    private readonly IEvents _events;
    private readonly ITaskOperations _tasks;

    public QueueTaskHearbeat(ILogTracer logTracer, ITaskOperations tasks, IEvents events) {
        _log = logTracer;
        _tasks = tasks;
        _events = events;
    }

    [Function("QueueTaskHeartbeat")]
    public async Async.Task Run([QueueTrigger("task-heartbeat", Connection = "AzureWebJobsStorage")] string msg) {
        _log.Info($"heartbeat: {msg}");

        var hb = JsonSerializer.Deserialize<TaskHeartbeatEntry>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}");

        var task = await _tasks.GetByTaskId(hb.TaskId);
        var log = _log.WithTag("TaskId", hb.TaskId.ToString());

        if (task == null) {
            log.Warning($"invalid task id");
            return;
        }

        var newTask = task with { Heartbeat = DateTimeOffset.UtcNow };
        var r = await _tasks.Replace(newTask);
        if (!r.IsOk) {
            log.WithHttpStatus(r.ErrorV).Error($"failed to replace with new task");
        }
        await _events.SendEvent(new EventTaskHeartbeat(newTask.JobId, newTask.TaskId, newTask.Config));
    }
}
