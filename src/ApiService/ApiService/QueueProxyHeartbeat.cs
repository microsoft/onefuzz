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
    public async Task Run([QueueTrigger("Proxy", Connection = "funcsamlrs3qn2nls_STORAGE")] string msg)
    {
        _logger.LogInformation($"heartbeat: {msg}");

        var hb = JsonSerializer.Deserialize<ProxyHeartbeat>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}");;

        var proxy = await _proxy.GetByProxyId(hb.ProxyId);

        if (proxy == null) {
            _logger.LogWarning($"invalid proxy id: {hb.ProxyId}");
            return;
        }
        var newProxy = proxy with { heartbeat = hb };

        await _proxy.Replace(newProxy);

    }
}
