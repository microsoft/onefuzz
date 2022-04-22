﻿using ApiService.OneFuzzLib.Orm;
using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service;


public interface IWebhookMessageLogOperations : IOrm<WebhookMessageLog>
{
    IAsyncEnumerable<WebhookMessageLog> SearchExpired();
    public Async.Task ProcessFromQueue(WebhookMessageQueueObj obj);
}


public class WebhookMessageLogOperations : Orm<WebhookMessageLog>, IWebhookMessageLogOperations
{
    const int EXPIRE_DAYS = 7;
    const int MAX_TRIES = 5;

    private readonly IQueue _queue;
    private readonly ILogTracer _log;
    private readonly IWebhookOperations _webhook;

    public WebhookMessageLogOperations(IStorage storage, IQueue queue, ILogTracer log, IWebhookOperations webhook) : base(storage, log)
    {
        _queue = queue;
        _log = log;
        _webhook = webhook;
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
            _log.WithTags(
                    new[] {
                        ("WebhookId", webhookLog.WebhookId.ToString()),
                        ("EventId", webhookLog.EventId.ToString()) }
                    ).
                Error($"invalid WebhookMessage queue state, not queuing. {webhookLog.WebhookId}:{webhookLog.EventId} - {webhookLog.State}");
        }
        else
        {
            await _queue.QueueObject("webhooks", obj, StorageType.Config, visibilityTimeout: visibilityTimeout);
        }
    }

    public async Async.Task ProcessFromQueue(WebhookMessageQueueObj obj)
    {
        var message = await GetWebhookMessageById(obj.WebhookId, obj.EventId);

        if (message == null)
        {
            _log.WithTags(
                new[] {
                    ("WebhookId", obj.WebhookId.ToString()),
                    ("EventId", obj.EventId.ToString()) }
            ).
            Error($"webhook message log not found for webhookId: {obj.WebhookId} and eventId: {obj.EventId}");
        }
        else
        {
            await Process(message);
        }
    }

    private async System.Threading.Tasks.Task Process(WebhookMessageLog message)
    {

        if (message.State == WebhookMessageState.Failed || message.State == WebhookMessageState.Succeeded)
        {
            _log.WithTags(
                new[] {
                    ("WebhookId", message.WebhookId.ToString()),
                    ("EventId", message.EventId.ToString()) }
            ).
            Error($"webhook message already handled. {message.WebhookId}:{message.EventId}");
            return;
        }

        var newMessage = message with { TryCount = message.TryCount + 1 };

        _log.Info($"sending webhook: {message.WebhookId}:{message.EventId}");
        var success = await Send(newMessage);
        if (success)
        {
            newMessage = newMessage with { State = WebhookMessageState.Succeeded };
            await Replace(newMessage);
            _log.Info($"sent webhook event {newMessage.WebhookId}:{newMessage.EventId}");
        }
        else if (newMessage.TryCount < MAX_TRIES)
        {
            newMessage = newMessage with { State = WebhookMessageState.Retrying };
            await Replace(newMessage);
            await QueueWebhook(newMessage);
            _log.Warning($"sending webhook event failed, re-queued {newMessage.WebhookId}:{newMessage.EventId}");
        }
        else
        {
            newMessage = newMessage with { State = WebhookMessageState.Failed };
            await Replace(newMessage);
            _log.Info($"sending webhook: {newMessage.WebhookId} event: {newMessage.EventId} failed {newMessage.TryCount} times.");
        }

    }

    private async Task<bool> Send(WebhookMessageLog message)
    {
        var webhook = await _webhook.GetByWebhookId(message.WebhookId);
        if (webhook == null)
        {
            _log.WithTags(
                new[] {
                    ("WebhookId", message.WebhookId.ToString()),
                }
            ).
            Error($"webhook not found for webhookId: {message.WebhookId}");
            return false;
        }

        try
        {
            return await _webhook.Send(message);
        }
        catch (Exception)
        {
            _log.WithTags(
                new[] {
                    ("WebhookId", message.WebhookId.ToString())
                }
            ).
            Error($"webhook send failed. {message.WebhookId}");
            return false;
        }

    }

    private void QueueObject(string v, WebhookMessageQueueObj obj, StorageType config, int? visibility_timeout)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<WebhookMessageLog> SearchExpired()
    {
        var expireTime = (DateTimeOffset.UtcNow - TimeSpan.FromDays(EXPIRE_DAYS)).ToString("o");

        var timeFilter = $"Timestamp lt datetime'{expireTime}'";
        return QueryAsync(filter: timeFilter);
    }

    public async Async.Task<WebhookMessageLog?> GetWebhookMessageById(Guid webhookId, Guid eventId)
    {
        var data = QueryAsync(filter: $"PartitionKey eq '{webhookId}' and Rowkey eq '{eventId}'");

        return await data.FirstOrDefaultAsync();
    }
}
