using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Microsoft.OneFuzz.Service;


public class TimerDaily
{
    private readonly ILogger _logger;

    private readonly IScalesetOperations _scalesets;

    private readonly IWebhookMessageLogOperations _webhookMessageLogs;

    public TimerDaily(ILoggerFactory loggerFactory, IScalesetOperations scalesets, IWebhookMessageLogOperations webhookMessageLogs)
    {
        _logger = loggerFactory.CreateLogger<QueueTaskHearbeat>();
        _scalesets = scalesets;
        _webhookMessageLogs = webhookMessageLogs;
    }

    [Function("TimerDaily")]
    public async Async.Task Run([TimerTrigger("1.00:00:00")] TimerInfo myTimer)
    {
        var scalesets = _scalesets.Search();
        await foreach (var scaleset in scalesets)
        {
            _logger.LogInformation($"updating scaleset configs: {scaleset.ScalesetId}");
            // todo: do ti in batches
            await _scalesets.Replace(scaleset with { NeedsConfigUpdate = true });
        }


        var expiredWebhookLogs = _webhookMessageLogs.SearchExpired();
        await foreach (var logEntry in expiredWebhookLogs)
        {
            _logger.LogInformation($"stopping expired webhook message log: {logEntry.WebhookId}:{logEntry.EventId}");
            await _webhookMessageLogs.Delete(logEntry);
        }
    }
}
