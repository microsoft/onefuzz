using System;
using Microsoft.Azure.Functions.Worker;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public class QueueWebhooks
{
    private readonly ILogTracerFactory _loggerFactory;

    public QueueWebhooks(ILogTracerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    [Function("QueueWebhooks")]
    public async Task Run([QueueTrigger("myqueue-items", Connection = "AzureWebJobsStorage")] string msg)
    {
        var log = _loggerFactory.MakeLogTracer(Guid.NewGuid());

        log.Info($"Webhook Message Queued: {msg}");

        var obj = JsonSerializer.Deserialize<WebhookMessageQueueObj>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}"); ;
        
        // WebhookMessageLog.process_from_queue(obj)
    }
}