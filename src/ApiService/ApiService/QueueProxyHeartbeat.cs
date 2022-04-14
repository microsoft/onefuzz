using Microsoft.Azure.Functions.Worker;
using System.Text.Json;
using System.Threading.Tasks;
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
    public async Task Run([QueueTrigger("myqueue-items", Connection = "AzureWebJobsStorage")] string msg)
    {
        var log = _loggerFactory.MakeLogTracer(Guid.NewGuid());

        log.Info($"heartbeat: {msg}");

        var hb = JsonSerializer.Deserialize<ProxyHeartbeat>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}"); ;
        var newHb = hb with { TimeStamp = DateTimeOffset.UtcNow };

        log.Tags["Proxy ID"] = newHb.ProxyId.ToString();


        var proxy = await _proxy.GetByProxyId(newHb.ProxyId);

        if (proxy == null)
        {
            log.Warning($"invalid proxy id: {newHb.ProxyId}");
            return;
        }
        var newProxy = proxy with { heartbeat = newHb };

        await _proxy.Replace(newProxy);

    }
}
