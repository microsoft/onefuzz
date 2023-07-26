using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
namespace Microsoft.OneFuzz.Service;

public interface IWebhookOperations : IOrm<Webhook> {
    Async.Task SendEvent(DownloadableEventMessage eventMessage);
    Async.Task<Webhook?> GetByWebhookId(Guid webhookId);
    Async.Task<OneFuzzResultVoid> Send(WebhookMessageLog messageLog);
    Task<EventPing> Ping(Webhook webhook);
    Task<OneFuzzResult<Tuple<string, string?>>> BuildMessage(Guid webhookId, Guid eventId, EventType eventType, BaseEvent webhookEvent, String? secretToken, WebhookMessageFormat? messageFormat);

}

public class WebhookOperations : Orm<Webhook>, IWebhookOperations {

    private readonly IHttpClientFactory _httpFactory;

    public WebhookOperations(IHttpClientFactory httpFactory, ILogger<WebhookOperations> log, IOnefuzzContext context)
        : base(log, context) {
        _httpFactory = httpFactory;
    }

    public async Async.Task SendEvent(DownloadableEventMessage eventMessage) {
        await foreach (var webhook in GetWebhooksCached()) {
            if (!webhook.EventTypes.Contains(eventMessage.EventType)) {
                continue;
            }
            await AddEvent(webhook, eventMessage);
        }
    }

    private async Async.Task AddEvent(Webhook webhook, DownloadableEventMessage eventMessage) {
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
                _logTracer.AddTags(tags);
                _logTracer.LogWarning("The WebhookMessageLog was too long for Azure Table. Truncating event data and trying again.");
                message = message with {
                    Event = truncatableEvent.Truncate(1000)
                };
                r = await _context.WebhookMessageLogOperations.Replace(message);
            }
            if (!r.IsOk) {
                _logTracer.AddHttpStatus(r.ErrorV);
                _logTracer.AddTags(tags);
                _logTracer.LogError("Failed to replace webhook message log {WebhookId} - {EventId}", webhook.WebhookId, eventMessage.EventId);
            }
        }

        await _context.WebhookMessageLogOperations.QueueWebhook(message);
    }

    public async Async.Task<OneFuzzResultVoid> Send(WebhookMessageLog messageLog) {
        var webhook = await GetByWebhookId(messageLog.WebhookId);
        if (webhook == null || webhook.Url == null) {
            return OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_FIND, $"Invalid Webhook. Webhook with WebhookId: {messageLog.WebhookId} Not Found");
        }

        var messageResult = await BuildMessage(webhookId: webhook.WebhookId, eventId: messageLog.EventId, eventType: messageLog.EventType, webhookEvent: messageLog.Event!, secretToken: webhook.SecretToken, messageFormat: webhook.MessageFormat);
        if (!messageResult.IsOk) {
            var tags = new List<(string, string)> {
                ("WebhookId", webhook.WebhookId.ToString()),
                ("EventId", messageLog.EventId.ToString()),
                ("EventType", messageLog.EventType.ToString())
            };

            _logTracer.AddTags(tags);
            _logTracer.LogError("Failed to build message for webhook.");
            return OneFuzzResultVoid.Error(messageResult.ErrorV);
        }

        var (data, digest) = messageResult.OkV;
        var headers = new Dictionary<string, string> { { "User-Agent", $"onefuzz-webhook {_context.ServiceConfiguration.OneFuzzVersion}" } };

        if (digest != null) {
            //make sure digest is lowercase to be backwards compatible with Python webhooks
            headers["X-Onefuzz-Digest"] = digest.ToLowerInvariant();
        }

        using var httpClient = _httpFactory.CreateClient();
        var client = new Request(httpClient);
        _logTracer.LogInformation("{WebhookId} - {data}", messageLog.WebhookId, data);
        using var response = await client.Post(url: webhook.Url, json: data, headers: headers);
        if (response.IsSuccessStatusCode) {
            _logTracer.LogInformation("Successfully sent webhook: {WebhookId}", messageLog.WebhookId);
            return OneFuzzResultVoid.Ok;
        }

        _logTracer
            .AddTags(new List<(string, string)> {
                ("StatusCode", response.StatusCode.ToString()),
                ("Content", await response.Content.ReadAsStringAsync())
            });
        _logTracer.LogInformation("Webhook not successful");

        return OneFuzzResultVoid.Error(ErrorCode.UNABLE_TO_SEND, $"Webhook not successful. Status Code: {response.StatusCode}");
    }

    public async Task<EventPing> Ping(Webhook webhook) {
        var ping = new EventPing(Guid.NewGuid());
        var instanceId = await _context.Containers.GetInstanceId();
        var instanceName = _context.Creds.GetInstanceName();
        var eventMessage = new EventMessage(
            ping.PingId, EventType.Ping, ping, instanceId, instanceName, DateTime.UtcNow
        );
        var downloadableEventMessage = await _context.Events.MakeDownloadable(eventMessage);

        await AddEvent(webhook, downloadableEventMessage);
        return ping;
    }

    // Not converting to bytes, as it's not neccessary in C#. Just keeping as string.
    public async Async.Task<OneFuzzResult<Tuple<string, string?>>> BuildMessage(Guid webhookId, Guid eventId, EventType eventType, BaseEvent webhookEvent, String? secretToken, WebhookMessageFormat? messageFormat) {
        var eventDataResult = await _context.Events.GetDownloadableEvent(eventId);
        if (!eventDataResult.IsOk) {
            return OneFuzzResult<Tuple<string, string?>>.Error(eventDataResult.ErrorV);
        }

        var eventData = eventDataResult.OkV;

        string data;
        var instanceId = await _context.Containers.GetInstanceId();
        var webhookMessage = new WebhookMessage(WebhookId: webhookId, EventId: eventId, EventType: eventType, Event: webhookEvent, InstanceId: instanceId, InstanceName: _context.Creds.GetInstanceName(), CreatedAt: eventData.CreatedAt, SasUrl: eventData.SasUrl);
        if (messageFormat != null && messageFormat == WebhookMessageFormat.EventGrid) {
            var eventGridMessage = new[] { new WebhookMessageEventGrid(Id: eventId, Data: webhookMessage, DataVersion: "2.0.0", Subject: _context.Creds.GetInstanceName(), EventType: eventType, EventTime: DateTimeOffset.UtcNow) };
            data = JsonSerializer.Serialize(eventGridMessage, options: EntityConverter.GetJsonSerializerOptions());
        } else {
            data = JsonSerializer.Serialize(webhookMessage, options: EntityConverter.GetJsonSerializerOptions());
        }

        string? digest = null;
        if (secretToken is not null) {
            using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secretToken));
            digest = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
        }

        return OneFuzzResult<Tuple<string, string?>>.Ok(new Tuple<string, string?>(data, digest));

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


    public WebhookMessageLogOperations(ILogger<WebhookMessageLogOperations> log, IOnefuzzContext context)
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
            _logTracer.LogError("invalid WebhookMessage queue state, not queuing. {WebhookId}:{EventId} - {State}", webhookLog.WebhookId, webhookLog.EventId, webhookLog.State);
        } else {
            if (!await _context.Queue.QueueObject("webhooks", obj, StorageType.Config, visibilityTimeout: visibilityTimeout)) {
                _logTracer.LogWarning("failed to queue object {WebhookId}:{EventId}", webhookLog.WebhookId, webhookLog.EventId);
            }
        }
    }

    public async Async.Task ProcessFromQueue(WebhookMessageQueueObj obj) {
        var message = await GetWebhookMessageById(obj.WebhookId, obj.EventId);

        if (message == null) {
            _logTracer.LogError("webhook message log not found for webhookId: {WebhookId} and eventId: {EventId}", obj.WebhookId, obj.EventId);
        } else {
            await Process(message);
        }
    }

    private async Async.Task Process(WebhookMessageLog message) {

        if (message.State == WebhookMessageState.Failed || message.State == WebhookMessageState.Succeeded) {
            _logTracer.LogError("webhook message already handled. {WebhookId}:{EventId}", message.WebhookId, message.EventId);
            return;
        }

        var newMessage = message with { TryCount = message.TryCount + 1 };

        _logTracer.LogInformation("sending webhook: {WebhookId}:{EventId}", message.WebhookId, message.EventId);
        var success = await Send(newMessage);
        if (success) {
            newMessage = newMessage with { State = WebhookMessageState.Succeeded };
            var r = await Replace(newMessage);
            if (!r.IsOk) {
                _logTracer.AddHttpStatus(r.ErrorV);
                _logTracer.LogError("failed to replace webhook message with {WebhookId}:{EventId} with Succeeded", newMessage.WebhookId, newMessage.EventId);
            }
            _logTracer.LogInformation("sent webhook event {WebhookId}:{EventId}", newMessage.WebhookId, newMessage.EventId);
        } else if (newMessage.TryCount < MAX_TRIES) {
            newMessage = newMessage with { State = WebhookMessageState.Retrying };
            var r = await Replace(newMessage);
            if (!r.IsOk) {
                _logTracer.AddHttpStatus(r.ErrorV);
                _logTracer.LogError("failed to replace webhook message with {WebhookId}:{EventId} with Retrying", newMessage.WebhookId, newMessage.EventId);
            }
            await QueueWebhook(newMessage);
            _logTracer.LogWarning("sending webhook event failed, re-queued {WebhookId}:{EventId}", newMessage.WebhookId, newMessage.EventId);
        } else {
            newMessage = newMessage with { State = WebhookMessageState.Failed };
            var r = await Replace(newMessage);
            if (!r.IsOk) {
                _logTracer.AddHttpStatus(r.ErrorV);
                _logTracer.LogError("failed to replace webhook message with EventId {WebhookId}:{EventId} with Failed", newMessage.WebhookId, newMessage.EventId);
            }
            var ex = new Exception($"Failed to send webhook: {newMessage.WebhookId} event: {newMessage.EventId} failed {newMessage.TryCount} times.");
            _logTracer.LogWarning(ex, "Failed to send webhook: {WebhookId} event: {EventId} failed {TryCount} times.", newMessage.WebhookId, newMessage.EventId, newMessage.TryCount);
        }
    }

    private async Async.Task<bool> Send(WebhookMessageLog message) {
        _logTracer.AddTag("WebhookId", message.WebhookId.ToString());
        var webhook = await _context.WebhookOperations.GetByWebhookId(message.WebhookId);
        if (webhook == null) {
            _logTracer.LogError("webhook not found for webhookId: {WebhookId}", message.WebhookId);
            return false;
        }
        try {
            var sendResult = await _context.WebhookOperations.Send(message);
            if (!sendResult.IsOk) {
                _logTracer.LogError("Send webhook:{error}", sendResult.ErrorV);
            }
            return sendResult.IsOk;
        } catch (Exception exc) {
            _logTracer.LogError("Send Webhook: {exception}", exc);
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
