using Microsoft.Azure.Functions.Worker;

namespace Microsoft.OneFuzz.Service.Functions;


public class TimerDaily {
    private readonly ILogTracer _log;

    private readonly IScalesetOperations _scalesets;

    private readonly IWebhookMessageLogOperations _webhookMessageLogs;

    public TimerDaily(ILogTracer log, IScalesetOperations scalesets, IWebhookMessageLogOperations webhookMessageLogs) {
        _log = log;
        _scalesets = scalesets;
        _webhookMessageLogs = webhookMessageLogs;
    }

    [Function("TimerDaily")]
    public async Async.Task Run([TimerTrigger("1.00:00:00")] TimerInfo myTimer) {
        var scalesets = _scalesets.Search();
        await foreach (var scaleset in scalesets) {
            _log.Info($"updating scaleset configs {scaleset.ScalesetId:Tag:ScalesetId}");
            // todo: do it in batches
            var r = await _scalesets.Replace(scaleset with { NeedsConfigUpdate = true });
            if (!r.IsOk) {
                _log.WithHttpStatus(r.ErrorV).Error($"failed to replace scaleset {scaleset.ScalesetId:Tag:ScalesetId} with NeedsConfigUpdate set to true");
            }
        }
        var expiredWebhookLogs = _webhookMessageLogs.SearchExpired();
        await foreach (var logEntry in expiredWebhookLogs) {
            _log.Info($"stopping expired webhook {logEntry.WebhookId:Tag:WebhookId} message log for {logEntry.EventId:Tag:EventId}");
            var r = await _webhookMessageLogs.Delete(logEntry);
            if (!r.IsOk) {
                _log.WithHttpStatus(r.ErrorV).Error($"failed to delete log entry for an expired webhook {logEntry.WebhookId:Tag:WebhookId} message for {logEntry.EventId:Tag:EventId}");
            }
        }
    }
}
