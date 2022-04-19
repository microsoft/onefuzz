using Microsoft.Azure.Functions.Worker;
using System.Text.Json;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public class QueueProxyHearbeat
{
    private readonly ILogTracer _log;

    private readonly IProxyOperations _proxy;

    public QueueProxyHearbeat(ILogTracer log, IProxyOperations proxy)
    {
        _log = log;
        _proxy = proxy;
    }

    [Function("QueueProxyHearbeat")]
    public async Async.Task Run([QueueTrigger("myqueue-items", Connection = "AzureWebJobsStorage")] string msg)
    {
        _log.Info($"heartbeat: {msg}");

        var hb = JsonSerializer.Deserialize<ProxyHeartbeat>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}"); ;
        var newHb = hb with { TimeStamp = DateTimeOffset.UtcNow };

        var proxy = await _proxy.GetByProxyId(newHb.ProxyId);

        var log = _log.WithTag("ProxyId", newHb.ProxyId.ToString());

        if (proxy == null)
        {
            log.Warning($"invalid proxy id: {newHb.ProxyId}");
            return;
        }
        var newProxy = proxy with { heartbeat = newHb };

        var r = await _proxy.Replace(newProxy);
        if (!r.IsOk)
        {
            var (status, reason) = r.ErrorV;
            log.Error($"Failed to replace proxy heartbeat record due to [{status}] {reason}");
        }
    }
}
