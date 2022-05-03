using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public class QueueWebhooks {
    private readonly ILogTracer _log;
    private readonly IWebhookMessageLogOperations _webhookMessageLog;
    public QueueWebhooks(ILogTracer log, IWebhookMessageLogOperations webhookMessageLog) {
        _log = log;
        _webhookMessageLog = webhookMessageLog;
    }

    [Function("QueueWebhooks")]
    public async Async.Task Run([QueueTrigger("myqueue-items", Connection = "AzureWebJobsStorage")] string msg) {

        _log.Info($"Webhook Message Queued: {msg}");

        var obj = JsonSerializer.Deserialize<WebhookMessageQueueObj>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}");

        await _webhookMessageLog.ProcessFromQueue(obj);
    }
}
