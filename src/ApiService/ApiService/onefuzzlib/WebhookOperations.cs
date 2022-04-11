using ApiService.OneFuzzLib.Orm;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;
using System;
using System.Collections.Generic;

namespace ApiService.OneFuzzLib;


public interface IWebhookMessageLogOperations : IOrm<WebhookMessageLog>
{

}


public class WebhookMessageLogOperations : Orm<WebhookMessageLog>, IWebhookMessageLogOperations
{
    record WebhookMessageQueueObj(
        Guid WebhookId,
        Guid EventId
        );

    private readonly IQueue _queue;
    private readonly ILogger _logger;
    public WebhookMessageLogOperations(IStorage storage, IQueue queue, ILoggerFactory loggerFactory) : base(storage)
    {
        _queue = queue;
        _logger = loggerFactory.CreateLogger<WebhookMessageLogOperations>(); ;
    }


    public async Async.Task QueueWebhook(WebhookMessageLog webhookLog)
    {
        var obj = new WebhookMessageQueueObj(webhookLog.WebhookId, webhookLog.EventId);

        TimeSpan? visibilityTimeout = webhookLog.State switch
        {
            WebhookMessageState.Queued => TimeSpan.Zero,
            WebhookMessageState.Retrying => TimeSpan.FromSeconds(30),
            _ => null
        };


        if (visibilityTimeout == null)
        {
            _logger.LogError($"invalid WebhookMessage queue state, not queuing. {webhookLog.WebhookId}:{webhookLog.EventId} - {webhookLog.State}");

        }
        else
        {
            await _queue.QueueObject("webhooks", obj, StorageType.Config, visibilityTimeout: visibilityTimeout);
        }
    }

    private void QueueObject(string v, WebhookMessageQueueObj obj, StorageType config, int? visibility_timeout)
    {
        throw new NotImplementedException();
    }
}


public interface IWebhookOperations
{
    Async.Task SendEvent(EventMessage eventMessage);
}

public class WebhookOperations : Orm<Webhook>, IWebhookOperations
{
    private readonly IWebhookMessageLogOperations _webhookMessageLogOperations;
    public WebhookOperations(IStorage storage, IWebhookMessageLogOperations webhookMessageLogOperations)
        : base(storage)
    {
        _webhookMessageLogOperations = webhookMessageLogOperations;
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

        await _webhookMessageLogOperations.Replace(message);
    }


    //todo: caching
    public IAsyncEnumerable<Webhook> GetWebhooksCached()
    {
        return QueryAsync();
    }

}
