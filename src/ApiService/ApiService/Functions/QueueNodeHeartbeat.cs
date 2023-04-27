using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service.Functions;


public class QueueNodeHearbeat {
    private readonly ILogTracer _log;

    private readonly IOnefuzzContext _context;

    public QueueNodeHearbeat(ILogTracer log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("QueueNodeHeartbeat")]
    public async Async.Task Run([QueueTrigger("node-heartbeat", Connection = "AzureWebJobsStorage")] string msg) {

        var nodes = _context.NodeOperations;
        var events = _context.Events;
        var metrics = _context.Metrics;

        _log.Info($"heartbeat: {msg}");
        var hb = JsonSerializer.Deserialize<NodeHeartbeatEntry>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}");
        var node = await nodes.GetByMachineId(hb.NodeId);

        if (node == null) {
            _log.Warning($"invalid {hb.NodeId:Tag:NodeId}");
            return;
        }

        var newNode = node with { Heartbeat = DateTimeOffset.UtcNow };
        var r = await nodes.Replace(newNode);
        if (!r.IsOk) {
            _log.WithHttpStatus(r.ErrorV).Error($"Failed to replace heartbeat: {hb.NodeId:Tag:NodeId}");
        }

        // TODO: do we still send event if we fail do update the table ?
        await events.SendEvent(new EventNodeHeartbeat(node.MachineId, node.ScalesetId, node.PoolName));
        if (await _context.FeatureManagerSnapshot.IsEnabledAsync(FeatureFlagConstants.EnableCustomMetricTelemetry)) {
            await metrics.SendMetric(1, new MetricNodeHeartbeat(node.MachineId, node.ScalesetId, node.PoolName, node.State));
        }

    }
}
