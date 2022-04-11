using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public class QueueProxyHearbeat
{
    private readonly ILogger _logger;

    private readonly IProxyOperations _proxy;

    public QueueProxyHearbeat(ILoggerFactory loggerFactory, IProxyOperations proxy)
    {
        _logger = loggerFactory.CreateLogger<QueueProxyHearbeat>();
        _proxy = proxy;
    }

    [Function("QueueProxyHearbeat")]
    public async Task Run([QueueTrigger("myqueue-items", Connection = "AzureWebJobsStorage")] string msg)
    {
        _logger.LogInformation($"heartbeat: {msg}");

        var hb = JsonSerializer.Deserialize<ProxyHeartbeat>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}"); ;
        var newHb = hb with { TimeStamp = DateTimeOffset.UtcNow };

        var proxy = await _proxy.GetByProxyId(newHb.ProxyId);

        if (proxy == null)
        {
            _logger.LogWarning($"invalid proxy id: {newHb.ProxyId}");
            return;
        }
        var newProxy = proxy with { heartbeat = newHb };

        await _proxy.Replace(newProxy);

    }
}
