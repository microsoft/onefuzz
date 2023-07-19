using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
namespace Microsoft.OneFuzz.Service.Functions;

public class QueueWebhooks {
    private readonly ILogger _log;
    private readonly IWebhookMessageLogOperations _webhookMessageLog;
    public QueueWebhooks(ILogger<QueueWebhooks> log, IWebhookMessageLogOperations webhookMessageLog) {
        _log = log;
        _webhookMessageLog = webhookMessageLog;
    }

    [Function("QueueWebhooks")]
    public async Async.Task Run([QueueTrigger("webhooks", Connection = "AzureWebJobsStorage")] string msg) {

        _log.LogInformation("Webhook Message Queued: {msg}", msg);

        var obj = JsonSerializer.Deserialize<WebhookMessageQueueObj>(msg, EntityConverter.GetJsonSerializerOptions()).EnsureNotNull($"wrong data {msg}");

        await _webhookMessageLog.ProcessFromQueue(obj);
    }
}
