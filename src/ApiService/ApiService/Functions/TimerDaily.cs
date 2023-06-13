using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service.Functions;


public class TimerDaily {
    private readonly ILogger _log;

    private readonly IScalesetOperations _scalesets;

    private readonly IWebhookMessageLogOperations _webhookMessageLogs;

    public TimerDaily(ILogger<TimerDaily> log, IScalesetOperations scalesets, IWebhookMessageLogOperations webhookMessageLogs) {
        _log = log;
        _scalesets = scalesets;
        _webhookMessageLogs = webhookMessageLogs;
    }

    [Function("TimerDaily")]
    public async Async.Task Run([TimerTrigger("1.00:00:00")] TimerInfo myTimer) {
        var scalesets = _scalesets.Search();
        await foreach (var scaleset in scalesets) {
            _log.LogInformation("updating scaleset configs {ScalesetId}", scaleset.ScalesetId);
            // todo: do it in batches
            var r = await _scalesets.Replace(scaleset with { NeedsConfigUpdate = true });
            if (!r.IsOk) {
                _log.AddHttpStatus(r.ErrorV);
                _log.LogError("failed to replace scaleset {ScalesetId} with NeedsConfigUpdate set to true", scaleset.ScalesetId);
            }
        }
        var expiredWebhookLogs = _webhookMessageLogs.SearchExpired();
        await foreach (var logEntry in expiredWebhookLogs) {
            _log.LogInformation("stopping expired webhook {WebhookId} message log for {EventId}", logEntry.WebhookId, logEntry.EventId);
            var r = await _webhookMessageLogs.Delete(logEntry);
            if (!r.IsOk) {
                _log.AddHttpStatus(r.ErrorV);
                _log.LogError("failed to delete log entry for an expired webhook {WebhookId} message for {EventId}", logEntry.WebhookId, logEntry.EventId);
            }
        }
    }
}
