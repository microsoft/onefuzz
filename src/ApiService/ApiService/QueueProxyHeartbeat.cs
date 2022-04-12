using System;
using Microsoft.Azure.Functions.Worker;
using System.Text.Json;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public class QueueProxyHearbeat
{
    private readonly ILogTracerFactory _loggerFactory;

    private readonly IProxyOperations _proxy;

    public QueueProxyHearbeat(ILogTracerFactory loggerFactory, IProxyOperations proxy)
    {
        _loggerFactory = loggerFactory;
        _proxy = proxy;
    }

    [Function("QueueProxyHearbeat")]
    public async Async.Task Run([QueueTrigger("myqueue-items", Connection = "AzureWebJobsStorage")] string msg)
    {
        var log = _loggerFactory.MakeLogTracer(Guid.NewGuid());

        log.Info($"heartbeat: {msg}");

        var hb = JsonSerializer.Deserialize<ProxyHeartbeat>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}"); ;
        var newHb = hb with { TimeStamp = DateTimeOffset.UtcNow };

        var proxy = await _proxy.GetByProxyId(newHb.ProxyId);

        var log2 = log.AddTag("ProxyId", newHb.ProxyId.ToString());

        if (proxy == null)
        {
            log2.Warning($"invalid proxy id: {newHb.ProxyId}");
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
