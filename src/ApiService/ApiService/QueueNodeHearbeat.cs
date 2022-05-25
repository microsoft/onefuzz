using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;


public class QueueNodeHearbeat {
    private readonly ILogTracer _log;

    private readonly IOnefuzzContext _context;

    public QueueNodeHearbeat(ILogTracer log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    //[Function("QueueNodeHearbeat")]
    public async Async.Task Run([QueueTrigger("node-heartbeat", Connection = "AzureWebJobsStorage")] string msg) {
        _log.Info($"heartbeat: {msg}");
        var nodes = _context.NodeOperations;
        var events = _context.Events;

        var hb = JsonSerializer.Deserialize<NodeHeartbeatEntry>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}");

        var node = await nodes.GetByMachineId(hb.NodeId);

        var log = _log.WithTag("NodeId", hb.NodeId.ToString());

        if (node == null) {
            log.Warning($"invalid node id: {hb.NodeId}");
            return;
        }

        var newNode = node with { Heartbeat = DateTimeOffset.UtcNow };

        var r = await nodes.Replace(newNode);

        if (!r.IsOk) {
            var (status, reason) = r.ErrorV;
            log.Error($"Failed to replace heartbeat info due to [{status}] {reason}");
        }

        // TODO: do we still send event if we fail do update the table ?
        await events.SendEvent(new EventNodeHeartbeat(node.MachineId, node.ScalesetId, node.PoolName));
    }
}
