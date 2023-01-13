using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IWebhookOperations : IOrm<Webhook> {
    Async.Task SendEvent(EventMessage eventMessage);
    Async.Task<Webhook?> GetByWebhookId(Guid webhookId);
    Async.Task<bool> Send(WebhookMessageLog messageLog);
    Task<EventPing> Ping(Webhook webhook);
}

public class WebhookOperations : Orm<Webhook>, IWebhookOperations {

    private readonly IHttpClientFactory _httpFactory;

    public WebhookOperations(IHttpClientFactory httpFactory, ILogTracer log, IOnefuzzContext context)
        : base(log, context) {
        _httpFactory = httpFactory;
    }

    async public Async.Task SendEvent(EventMessage eventMessage) {
        await foreach (var webhook in GetWebhooksCached()) {
            if (!webhook.EventTypes.Contains(eventMessage.EventType)) {
                continue;
            }
            await AddEvent(webhook, eventMessage);
        }
    }

    async private Async.Task AddEvent(Webhook webhook, EventMessage eventMessage) {
        (string, string)[] tags = { ("WebhookId", webhook.WebhookId.ToString()), ("EventId", eventMessage.EventId.ToString()) };

        var message = new WebhookMessageLog(
             EventId: eventMessage.EventId,
             EventType: eventMessage.EventType,
             Event: eventMessage.Event,
             InstanceId: eventMessage.InstanceId,
             InstanceName: eventMessage.InstanceName,
             WebhookId: webhook.WebhookId,
             TryCount: 0
            );

        var r = await _context.WebhookMessageLogOperations.Replace(message);
        if (!r.IsOk) {
            if (r.ErrorV.Reason.Contains("The entity is larger than the maximum allowed size") && eventMessage.Event is ITruncatable<BaseEvent> truncatableEvent) {
                _logTracer.WithTags(tags).Warning($"The WebhookMessageLog was too long. Truncating event data and trying again.");
                var truncatedEventMessage = message with {
                    Event = truncatableEvent.Truncate(1000)
                };
                r = await _context.WebhookMessageLogOperations.Replace(truncatedEventMessage);
            }
            if (!r.IsOk) {
                _logTracer.WithHttpStatus(r.ErrorV).WithTags(tags).Error($"Failed to replace webhook message log {webhook.WebhookId:Tag:WebhookId} - {eventMessage.EventId:Tag:EventId}");
            }
        }

        await _context.WebhookMessageLogOperations.QueueWebhook(message);
    }

    public async Async.Task<bool> Send(WebhookMessageLog messageLog) {
        var webhook = await GetByWebhookId(messageLog.WebhookId);
        if (webhook == null || webhook.Url == null) {
            throw new Exception($"Invalid Webhook. Webhook with WebhookId: {messageLog.WebhookId} Not Found");
        }

        var (data, digest) = await BuildMessage(webhookId: webhook.WebhookId, eventId: messageLog.EventId, eventType: messageLog.EventType, webhookEvent: messageLog.Event!, secretToken: webhook.SecretToken, messageFormat: webhook.MessageFormat);

        var headers = new Dictionary<string, string> { { "User-Agent", $"onefuzz-webhook {_context.ServiceConfiguration.OneFuzzVersion}" } };

        if (digest != null) {
            //make sure digest is lowercase to be backwards compatible with Python webhooks
            headers["X-Onefuzz-Digest"] = digest.ToLowerInvariant();
        }

        using var httpClient = _httpFactory.CreateClient();
        var client = new Request(httpClient);
        _logTracer.Info($"{messageLog.WebhookId:Tag:WebhookId} - {data}");
        using var response = await client.Post(url: webhook.Url, json: data, headers: headers);
        if (response.IsSuccessStatusCode) {
            _logTracer.Info($"Successfully sent webhook: {messageLog.WebhookId:Tag:WebhookId}");
            return true;
        }

        _logTracer
            .WithTags(new List<(string, string)> {
                ("StatusCode", response.StatusCode.ToString()),
                ("Content", await response.Content.ReadAsStringAsync())
            })
            .Info($"Webhook not successful");

        return false;
    }

    public async Task<EventPing> Ping(Webhook webhook) {
        var ping = new EventPing(Guid.NewGuid());
        var instanceId = await _context.Containers.GetInstanceId();
        var instanceName = _context.Creds.GetInstanceName();
        await AddEvent(webhook, new EventMessage(Guid.NewGuid(), EventType.Ping, ping, instanceId, instanceName));
        return ping;
    }

    // Not converting to bytes, as it's not neccessary in C#. Just keeping as string.
    public async Async.Task<Tuple<string, string?>> BuildMessage(Guid webhookId, Guid eventId, EventType eventType, BaseEvent webhookEvent, String? secretToken, WebhookMessageFormat? messageFormat) {
        string data = "";
        if (messageFormat != null && messageFormat == WebhookMessageFormat.EventGrid) {
            var eventGridMessage = new[] { new WebhookMessageEventGrid(Id: eventId, Data: webhookEvent, DataVersion: "1.0.0", Subject: _context.Creds.GetInstanceName(), EventType: eventType, EventTime: DateTimeOffset.UtcNow) };
            data = JsonSerializer.Serialize(eventGridMessage, options: EntityConverter.GetJsonSerializerOptions());
        } else {
            var instanceId = await _context.Containers.GetInstanceId();
            var webhookMessage = new WebhookMessage(WebhookId: webhookId, EventId: eventId, EventType: eventType, Event: webhookEvent, InstanceId: instanceId, InstanceName: _context.Creds.GetInstanceName());

            data = JsonSerializer.Serialize(webhookMessage, options: EntityConverter.GetJsonSerializerOptions());
        }

        string? digest = null;
        if (secretToken is not null) {
            using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secretToken));
            digest = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
        }
        return new Tuple<string, string?>(data, digest);

    }

    public async Async.Task<Webhook?> GetByWebhookId(Guid webhookId) {
        var data = QueryAsync(filter: $"PartitionKey eq '{webhookId}'");

        return await data.FirstOrDefaultAsync();
    }

    //todo: caching
    public IAsyncEnumerable<Webhook> GetWebhooksCached() {
        return QueryAsync();
    }

}

public interface IWebhookMessageLogOperations : IOrm<WebhookMessageLog> {
    IAsyncEnumerable<WebhookMessageLog> SearchExpired();
    public Async.Task ProcessFromQueue(WebhookMessageQueueObj obj);
    public Async.Task QueueWebhook(WebhookMessageLog webhookLog);
}


public class WebhookMessageLogOperations : Orm<WebhookMessageLog>, IWebhookMessageLogOperations {
    const int EXPIRE_DAYS = 7;
    const int MAX_TRIES = 5;


    public WebhookMessageLogOperations(IHttpClientFactory httpFactory, ILogTracer log, IOnefuzzContext context)
        : base(log, context) {

    }


    public async Async.Task QueueWebhook(WebhookMessageLog webhookLog) {
        var obj = new WebhookMessageQueueObj(webhookLog.WebhookId, webhookLog.EventId);

        TimeSpan? visibilityTimeout = webhookLog.State switch {
            WebhookMessageState.Queued => TimeSpan.Zero,
            WebhookMessageState.Retrying => TimeSpan.FromSeconds(30),
            _ => null
        };

        if (visibilityTimeout == null) {
            _logTracer.Error($"invalid WebhookMessage queue state, not queuing. {webhookLog.WebhookId:Tag:WebhookId}:{webhookLog.EventId:Tag:EventId} - {webhookLog.State:Tag:State}");
        } else {
            if (!await _context.Queue.QueueObject("webhooks", obj, StorageType.Config, visibilityTimeout: visibilityTimeout)) {
                _logTracer.Warning($"failed to queue object {webhookLog.WebhookId:Tag:WebhookId}:{webhookLog.EventId:Tag:EventId}");
            }
        }
    }

    public async Async.Task ProcessFromQueue(WebhookMessageQueueObj obj) {
        var message = await GetWebhookMessageById(obj.WebhookId, obj.EventId);

        if (message == null) {
            _logTracer.Error($"webhook message log not found for webhookId: {obj.WebhookId:Tag:WebhookId} and eventId: {obj.EventId:Tag:EventId}");
        } else {
            await Process(message);
        }
    }

    private async Async.Task Process(WebhookMessageLog message) {

        if (message.State == WebhookMessageState.Failed || message.State == WebhookMessageState.Succeeded) {
            _logTracer.Error($"webhook message already handled. {message.WebhookId:Tag:WebhookId}:{message.EventId:Tag:EventId}");
            return;
        }

        var newMessage = message with { TryCount = message.TryCount + 1 };

        _logTracer.Info($"sending webhook: {message.WebhookId:Tag:WebhookId}:{message.EventId:Tag:EventId}");
        var success = await Send(newMessage);
        if (success) {
            newMessage = newMessage with { State = WebhookMessageState.Succeeded };
            var r = await Replace(newMessage);
            if (!r.IsOk) {
                _logTracer.WithHttpStatus(r.ErrorV).Error($"failed to replace webhook message with {newMessage.WebhookId:Tag:WebhookId}:{newMessage.EventId:Tag:EventId} with Succeeded");
            }
            _logTracer.Info($"sent webhook event {newMessage.WebhookId}:{newMessage.EventId}");
        } else if (newMessage.TryCount < MAX_TRIES) {
            newMessage = newMessage with { State = WebhookMessageState.Retrying };
            var r = await Replace(newMessage);
            if (!r.IsOk) {
                _logTracer.WithHttpStatus(r.ErrorV).Error($"failed to replace webhook message with {newMessage.WebhookId:Tag:WebhookId}:{newMessage.EventId:Tag:EventId} with Retrying");
            }
            await QueueWebhook(newMessage);
            _logTracer.Warning($"sending webhook event failed, re-queued {newMessage.WebhookId:Tag:WebhookId}:{newMessage.EventId:Tag:EventId}");
        } else {
            newMessage = newMessage with { State = WebhookMessageState.Failed };
            var r = await Replace(newMessage);
            if (!r.IsOk) {
                _logTracer.WithHttpStatus(r.ErrorV).Error($"failed to replace webhook message with EventId {newMessage.WebhookId:Tag:WebhookId}:{newMessage.EventId:Tag:EventId} with Failed");
            }
            _logTracer.Info($"sending webhook: {newMessage.WebhookId:Tag:WebhookId} event: {newMessage.EventId:Tag:EventId} failed {newMessage.TryCount:Tag:TryCount} times.");
        }
    }

    private async Async.Task<bool> Send(WebhookMessageLog message) {
        var log = _logTracer.WithTag("WebhookId", message.WebhookId.ToString());
        var webhook = await _context.WebhookOperations.GetByWebhookId(message.WebhookId);
        if (webhook == null) {
            log.Error($"webhook not found for webhookId: {message.WebhookId:Tag:WebhookId}");
            return false;
        }
        try {
            return await _context.WebhookOperations.Send(message);
        } catch (Exception exc) {
            log.Exception(exc);
            return false;
        }

    }

    public IAsyncEnumerable<WebhookMessageLog> SearchExpired() {
        var expireTime = DateTimeOffset.UtcNow - TimeSpan.FromDays(EXPIRE_DAYS);

        var timeFilter = Query.TimestampOlderThan(expireTime);
        return QueryAsync(filter: timeFilter);
    }

    public async Async.Task<WebhookMessageLog?> GetWebhookMessageById(Guid webhookId, Guid eventId) {
        var data = QueryAsync(filter: Query.SingleEntity(webhookId.ToString(), eventId.ToString()));

        return await data.FirstOrDefaultAsync();
    }
}
