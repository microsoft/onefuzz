using ApiService.OneFuzzLib.Orm;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.OneFuzz.Service;

public interface IWebhookOperations
{
    Async.Task SendEvent(EventMessage eventMessage);
    Async.Task<Webhook?> GetByWebhookId(Guid webhookId);
    Async.Task Send(WebhookMessageLog messageLog);
}

public class WebhookOperations : Orm<Webhook>, IWebhookOperations
{
    private readonly IWebhookMessageLogOperations _webhookMessageLogOperations;
    private readonly ILogTracer _log;
    
    public WebhookOperations(IStorage storage, IWebhookMessageLogOperations webhookMessageLogOperations, ILogTracer log)
        : base(storage)
    {
        _webhookMessageLogOperations = webhookMessageLogOperations;
        _log = log;
    }

    async public Async.Task SendEvent(EventMessage eventMessage)
    {
        await foreach (var webhook in GetWebhooksCached())
        {
            if (!webhook.EventTypes.Contains(eventMessage.EventType))
            {
                continue;
            }
            await AddEvent(webhook, eventMessage);
        }
    }

    async private Async.Task AddEvent(Webhook webhook, EventMessage eventMessage)
    {
        var message = new WebhookMessageLog(
             EventId: eventMessage.EventId,
             EventType: eventMessage.EventType,
             Event: eventMessage.Event,
             InstanceId: eventMessage.InstanceId,
             InstanceName: eventMessage.InstanceName,
             WebhookId: webhook.WebhookId
            );

        var r = await _webhookMessageLogOperations.Replace(message);
        if (!r.IsOk)
        {
            var (status, reason) = r.ErrorV;
            _log.Error($"Failed to replace webhook message log due to [{status}] {reason}");
        }
    }

    public async Async.Task Send(WebhookMessageLog messageLog)
    {
        var webhook = await GetByWebhookId(messageLog.WebhookId);
        if (webhook == null)
        {
            throw new Exception($"Webhook with WebhookId: {messageLog.WebhookId} Not Found");
        }

    }

    // public Tuple<byte, String?> BuildMessage(Guid webhookId, Guid eventId, EventType eventType, Event webhookEvent, String? SeretToken, WebhookMessageFormat? messageFormat)
    // {
    //     if (messageFormat != null && messageFormat == WebhookMessageFormat.EventGrid)
    //     {
    //         var eventGridMessage = new WebhookMessageEventGrid(Id: eventId, data: webhookEvent, DataVersion: "1.0.0", Subject: )
    //         // var decoded = [JsonSerializer.Serialize()]
    //     }
    // }

    public async Async.Task<Webhook?> GetByWebhookId(Guid webhookId)
    {
        var data = QueryAsync(filter: $"PartitionKey eq '{webhookId}'");

        return await data.FirstOrDefaultAsync();
    }

    //todo: caching
    public IAsyncEnumerable<Webhook> GetWebhooksCached()
    {
        return QueryAsync();
    }

}
