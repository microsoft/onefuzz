using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;


public class QueueTaskHearbeat
{
    private readonly ILogger _logger;

    private readonly IEvents _events;
    private readonly ITaskOperations _tasks;

    public QueueTaskHearbeat(ILoggerFactory loggerFactory, ITaskOperations tasks, IEvents events)
    {
        _logger = loggerFactory.CreateLogger<QueueTaskHearbeat>();
        _tasks = tasks;
        _events = events;
    }

    [Function("QueueNodeHearbeat")]
    public async Tasks.Task Run([QueueTrigger("myqueue-items", Connection = "AzureWebJobsStorage")] string msg)
    {
        _logger.LogInformation($"heartbeat: {msg}");

        var hb = JsonSerializer.Deserialize<TaskHeartbeatEntry>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}");

        var task = await _tasks.GetByTaskId(hb.TaskId);

        if (task == null)
        {
            _logger.LogWarning($"invalid task id: {hb.TaskId}");
            return;
        }

        var newTask = task with { Heartbeat = DateTimeOffset.UtcNow };
        await _tasks.Replace(newTask);
        await _events.SendEvent(new EventTaskHeartbeat(newTask.JobId, newTask.TaskId, newTask.Config));
    }
}
