using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
namespace Microsoft.OneFuzz.Service.Functions;


public class QueueNodeHearbeat {
    private readonly ILogger _log;

    private readonly IOnefuzzContext _context;

    public QueueNodeHearbeat(ILogger<QueueNodeHearbeat> log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("QueueNodeHeartbeat")]
    public async Async.Task Run([QueueTrigger("node-heartbeat", Connection = "AzureWebJobsStorage")] string msg) {

        var nodes = _context.NodeOperations;
        var events = _context.Events;
        var metrics = _context.Metrics;

        _log.LogInformation("heartbeat: {msg}", msg);
        var hb = JsonSerializer.Deserialize<NodeHeartbeatEntry>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}");
        var node = await nodes.GetByMachineId(hb.NodeId);

        if (node == null) {
            _log.LogWarning("invalid {NodeId}", hb.NodeId);
            return;
        }

        var newNode = node with { Heartbeat = DateTimeOffset.UtcNow };
        var r = await nodes.Replace(newNode);
        if (!r.IsOk) {
            _log.AddHttpStatus(r.ErrorV);
            _log.LogError("Failed to replace heartbeat: {NodeId}", hb.NodeId);
        }

        var nodeHeartbeatEvent = new EventNodeHeartbeat(node.MachineId, node.ScalesetId, node.PoolName, node.State);
        // TODO: do we still send event if we fail do update the table ?
        await events.SendEvent(nodeHeartbeatEvent);
        metrics.SendMetric(1, nodeHeartbeatEvent);


    }
}
