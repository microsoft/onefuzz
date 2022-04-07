using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Text.Json;
using Azure.Data.Tables;
using System.Threading.Tasks;
using Azure;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;


public class QueueNodeHearbeat
{
    private readonly ILogger _logger;
    private readonly IStorageProvider _storageProvider;

    private readonly IEvents _events;

    public QueueNodeHearbeat(ILoggerFactory loggerFactory, IStorageProvider storageProvider, IEvents events)
    {
        _logger = loggerFactory.CreateLogger<QueueNodeHearbeat>();
        _storageProvider = storageProvider;
        _events = events;
    }

    [Function("QueueNodeHearbeat")]
    public async Task Run([QueueTrigger("myqueue-items", Connection = "AzureWebJobsStorage")] string msg)
    {
        _logger.LogInformation($"heartbeat: {msg}");

        var hb = JsonSerializer.Deserialize<NodeHeartbeatEntry>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}");

        var node = await Node.GetByMachineId(_storageProvider, hb.NodeId);

        if (node == null) {
            _logger.LogWarning($"invalid node id: {hb.NodeId}");
            return;
        }

        var newNode = node with { Heartbeat = DateTimeOffset.UtcNow };

        await _storageProvider.Replace(newNode);

        await _events.SendEvent(new EventNodeHeartbeat(node.MachineId, node.ScalesetId, node.PoolName));
    }
}
