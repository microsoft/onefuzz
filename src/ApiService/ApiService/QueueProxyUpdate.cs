using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using ApiService;

namespace Microsoft.OneFuzz.Service;

public class QueueProxyUpdate 
{
    private readonly ILogger _logger;

    public QueueProxyUpdate(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<QueueProxyUpdate>();
    }

    [Function("QueueProxyUpdate")]
    public void Run([QueueTrigger("myqueue-items", Connection = "AzureWebJobsStorage")] string msg)
    {
        var hb = JsonSerializer.Deserialize<ProxyHeartbeat>(msg);
        
        _logger.LogInformation($"heartbeat: {msg}");
    }
}
