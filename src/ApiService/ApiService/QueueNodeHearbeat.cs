using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Text.Json;
using Azure.Data.Tables;
using System.Threading.Tasks;
using Azure;
using ApiService.onefuzzlib.orm;
using ApiService;

namespace Microsoft.OneFuzz.Service;


enum HeartbeatType
{
    MachineAlive,
    TaskAlive,
}


public class QueueNodeHearbeat
{

    private readonly ILogger _logger;
    private readonly IStorageProvider _storageProvider;

    public QueueNodeHearbeat(ILoggerFactory loggerFactory, IStorageProvider storageProvider)
    {
        _logger = loggerFactory.CreateLogger<QueueNodeHearbeat>();
        _storageProvider = storageProvider;
    }

    [Function("QueueNodeHearbeat")]
    public async Task Run([QueueTrigger("myqueue-items", Connection = "AzureWebJobsStorage")] string msg)
    {
        var hb = JsonSerializer.Deserialize<NodeHeartbeatEntry>(msg);

        var node = await Node.GetByMachineId(_storageProvider, hb.NodeId);

        if (node == null) {
            _logger.LogWarning($"invalid node id: {hb.NodeId}");
            return;
        }

        var newNode = node with { Heartbeat = DateTimeOffset.UtcNow };

        await _storageProvider.Replace(newNode);

        //send_event(
        //    EventNodeHeartbeat(
        //        machine_id = node.machine_id,
        //        scaleset_id = node.scaleset_id,
        //        pool_name = node.pool_name,
        //    )
        //)

        _logger.LogInformation($"heartbeat: {msg}");
    }
}
