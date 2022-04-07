using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;


public class QueueNodeHearbeat
{
    private readonly ILogger _logger;

    private readonly IEvents _events;
    private readonly INodeOperations _nodes;

    public QueueNodeHearbeat(ILoggerFactory loggerFactory, INodeOperations nodes, IEvents events)
    {
        _logger = loggerFactory.CreateLogger<QueueNodeHearbeat>();
        _nodes = nodes;
        _events = events;
    }

    [Function("QueueNodeHearbeat")]
    public async Task Run([QueueTrigger("myqueue-items", Connection = "AzureWebJobsStorage")] string msg)
    {
        _logger.LogInformation($"heartbeat: {msg}");

        var hb = JsonSerializer.Deserialize<NodeHeartbeatEntry>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}");

        var node = await _nodes.GetByMachineId(hb.NodeId);

        if (node == null)
        {
            _logger.LogWarning($"invalid node id: {hb.NodeId}");
            return;
        }

        var newNode = node with { Heartbeat = DateTimeOffset.UtcNow };

        await _nodes.Replace(newNode);

        await _events.SendEvent(new EventNodeHeartbeat(node.MachineId, node.ScalesetId, node.PoolName));
    }
}
