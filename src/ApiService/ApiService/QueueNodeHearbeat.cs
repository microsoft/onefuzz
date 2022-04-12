using System;
using Microsoft.Azure.Functions.Worker;
using System.Text.Json;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;


public class QueueNodeHearbeat
{
    private readonly ILogTracerFactory _loggerFactory;

    private readonly IEvents _events;
    private readonly INodeOperations _nodes;

    public QueueNodeHearbeat(ILogTracerFactory loggerFactory, INodeOperations nodes, IEvents events)
    {
        _loggerFactory = loggerFactory;
        _nodes = nodes;
        _events = events;
    }

    [Function("QueueNodeHearbeat")]
    public async Async.Task Run([QueueTrigger("myqueue-items", Connection = "AzureWebJobsStorage")] string msg)
    {
        var log = _loggerFactory.MakeLogTracer(Guid.NewGuid());
        log.Info($"heartbeat: {msg}");

        var hb = JsonSerializer.Deserialize<NodeHeartbeatEntry>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}");

        var node = await _nodes.GetByMachineId(hb.NodeId);

        var log = _log.WithTag("NodeId", hb.NodeId.ToString());

        if (node == null)
        {
            log.Warning($"invalid node id: {hb.NodeId}");
            return;
        }

        var newNode = node with { Heartbeat = DateTimeOffset.UtcNow };

        var r = await _nodes.Replace(newNode);

        if (!r.IsOk)
        {
            var (status, reason) = r.ErrorV;
            log.Error($"Failed to replace heartbeat info due to [{status}] {reason}");
        }

        // TODO: do we still send event if we fail do update the table ?
        await _events.SendEvent(new EventNodeHeartbeat(node.MachineId, node.ScalesetId, node.PoolName));
    }
}
