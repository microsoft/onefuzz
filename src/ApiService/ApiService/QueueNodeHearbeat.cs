using Microsoft.Azure.Functions.Worker;
using System.Text.Json;
using System.Threading.Tasks;
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
    public async Task Run([QueueTrigger("myqueue-items", Connection = "AzureWebJobsStorage")] string msg)
    {
        var log = _loggerFactory.MakeLogTracer(Guid.NewGuid());
        log.Info($"heartbeat: {msg}");

        var hb = JsonSerializer.Deserialize<NodeHeartbeatEntry>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}");

        var node = await _nodes.GetByMachineId(hb.NodeId);

        if (node == null)
        {
            log.Warning($"invalid node id: {hb.NodeId}");
            return;
        }

        var newNode = node with { Heartbeat = DateTimeOffset.UtcNow };

        await _nodes.Replace(newNode);

        await _events.SendEvent(new EventNodeHeartbeat(node.MachineId, node.ScalesetId, node.PoolName));
    }
}
