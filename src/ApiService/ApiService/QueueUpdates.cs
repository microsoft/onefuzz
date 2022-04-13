using Microsoft.Azure.Functions.Worker;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public class QueueUpdates
{
    private readonly ILogTracerFactory _loggerFactory;

    private IUpdates _updates;

    public QueueUpdates(ILogTracerFactory loggerFactory, IUpdates updates)
    {
        _loggerFactory = loggerFactory;
        _updates = updates;
    }

    [Function("QueueUpdates")]
    public async Task Run(
        [QueueTrigger("update-queue-refactored", Connection = "AzureWebJobsStorage")] string msg)
    {
        var log = _loggerFactory.MakeLogTracer(Guid.NewGuid());

        var update = JsonSerializer.Deserialize<Update>(msg, EntityConverter.GetJsonSerializerOptions());
        if (update == null)
        {
            log.Error($"Failed to deserialize message into {typeof(Update)}: {msg}");
            return;
        }

        await _updates.ExecuteUpdate(update);
    }
}
