using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
namespace Microsoft.OneFuzz.Service.Functions;

public class QueueProxyHearbeat {
    private readonly ILogger _log;

    private readonly IProxyOperations _proxy;

    public QueueProxyHearbeat(ILogger<QueueProxyHearbeat> log, IProxyOperations proxy) {
        _log = log;
        _proxy = proxy;
    }

    [Function("QueueProxyUpdate")]
    public async Async.Task Run([QueueTrigger("proxy", Connection = "AzureWebJobsStorage")] string msg) {
        _log.LogInformation("heartbeat: {msg}", msg);

        var hb = JsonSerializer.Deserialize<ProxyHeartbeat>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}"); ;
        var newHb = hb with { TimeStamp = DateTimeOffset.UtcNow };

        var proxy = await _proxy.GetByProxyId(newHb.ProxyId);

        if (proxy == null) {
            _log.LogWarning("invalid proxy id: {ProxyId}", newHb.ProxyId);
            return;
        }
        var newProxy = proxy with { Heartbeat = newHb };

        var r = await _proxy.Replace(newProxy);
        if (!r.IsOk) {
            _log.AddHttpStatus(r.ErrorV);
            _log.LogError("Failed to replace proxy heartbeat {ProxyId}", newHb.ProxyId);
        }
    }
}
