using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using ApiService.OneFuzzLib.Orm;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IWebhookOperations {
    Async.Task SendEvent(EventMessage eventMessage);
    Async.Task<Webhook?> GetByWebhookId(Guid webhookId);
    Async.Task<bool> Send(WebhookMessageLog messageLog);
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
            var (status, reason) = r.ErrorV;
            _logTracer.Error($"Failed to replace webhook message log due to [{status}] {reason}");
        }
    }

    public async Async.Task<bool> Send(WebhookMessageLog messageLog) {
        var webhook = await GetByWebhookId(messageLog.WebhookId);
        if (webhook == null || webhook.Url == null) {
            throw new Exception($"Invalid Webhook. Webhook with WebhookId: {messageLog.WebhookId} Not Found");
        }

        var (data, digest) = await BuildMessage(webhookId: webhook.WebhookId, eventId: messageLog.EventId, eventType: messageLog.EventType, webhookEvent: messageLog.Event, secretToken: webhook.SecretToken, messageFormat: webhook.MessageFormat);

        var headers = new Dictionary<string, string> { { "User-Agent", $"onefuzz-webhook {_context.ServiceConfiguration.OneFuzzVersion}" } };

        if (digest != null) {
            headers["X-Onefuzz-Digest"] = digest;
        }

        var client = new Request(_httpFactory.CreateClient());
        _logTracer.Info(data);
        var response = client.Post(url: webhook.Url, json: data, headers: headers);
        var result = response.Result;
        if (result.StatusCode == HttpStatusCode.Accepted) {
            return true;
        }
        return false;
    }

    // Not converting to bytes, as it's not neccessary in C#. Just keeping as string.
    public async Async.Task<Tuple<string, string?>> BuildMessage(Guid webhookId, Guid eventId, EventType eventType, BaseEvent webhookEvent, String? secretToken, WebhookMessageFormat? messageFormat) {
        var entityConverter = new EntityConverter();
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
        var hmac = HMAC.Create("HMACSHA512");
        if (secretToken != null && hmac != null) {
            hmac.Key = System.Text.Encoding.UTF8.GetBytes(secretToken);
            digest = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data)));
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
            _logTracer.WithTags(
                    new[] {
                        ("WebhookId", webhookLog.WebhookId.ToString()),
                        ("EventId", webhookLog.EventId.ToString()) }
                    ).
                Error($"invalid WebhookMessage queue state, not queuing. {webhookLog.WebhookId}:{webhookLog.EventId} - {webhookLog.State}");
        } else {
            await _context.Queue.QueueObject("webhooks", obj, StorageType.Config, visibilityTimeout: visibilityTimeout);
        }
    }

    public async Async.Task ProcessFromQueue(WebhookMessageQueueObj obj) {
        var message = await GetWebhookMessageById(obj.WebhookId, obj.EventId);

        if (message == null) {
            _logTracer.WithTags(
                new[] {
                    ("WebhookId", obj.WebhookId.ToString()),
                    ("EventId", obj.EventId.ToString()) }
            ).
            Error($"webhook message log not found for webhookId: {obj.WebhookId} and eventId: {obj.EventId}");
        } else {
            await Process(message);
        }
    }

    private async Async.Task Process(WebhookMessageLog message) {

        if (message.State == WebhookMessageState.Failed || message.State == WebhookMessageState.Succeeded) {
            _logTracer.WithTags(
                new[] {
                    ("WebhookId", message.WebhookId.ToString()),
                    ("EventId", message.EventId.ToString()) }
            ).
            Error($"webhook message already handled. {message.WebhookId}:{message.EventId}");
            return;
        }

        var newMessage = message with { TryCount = message.TryCount + 1 };

        _logTracer.Info($"sending webhook: {message.WebhookId}:{message.EventId}");
        var success = await Send(newMessage);
        if (success) {
            newMessage = newMessage with { State = WebhookMessageState.Succeeded };
            await Replace(newMessage);
            _logTracer.Info($"sent webhook event {newMessage.WebhookId}:{newMessage.EventId}");
        } else if (newMessage.TryCount < MAX_TRIES) {
            newMessage = newMessage with { State = WebhookMessageState.Retrying };
            await Replace(newMessage);
            await QueueWebhook(newMessage);
            _logTracer.Warning($"sending webhook event failed, re-queued {newMessage.WebhookId}:{newMessage.EventId}");
        } else {
            newMessage = newMessage with { State = WebhookMessageState.Failed };
            await Replace(newMessage);
            _logTracer.Info($"sending webhook: {newMessage.WebhookId} event: {newMessage.EventId} failed {newMessage.TryCount} times.");
        }

    }

    private async Async.Task<bool> Send(WebhookMessageLog message) {
        var webhook = await _context.WebhookOperations.GetByWebhookId(message.WebhookId);
        if (webhook == null) {
            _logTracer.WithTags(
                new[] {
                    ("WebhookId", message.WebhookId.ToString()),
                }
            ).
            Error($"webhook not found for webhookId: {message.WebhookId}");
            return false;
        }

        try {
            return await _context.WebhookOperations.Send(message);
        } catch (Exception exc) {
            _logTracer.WithTags(
                new[] {
                    ("WebhookId", message.WebhookId.ToString())
                }
            ).
            Exception(exc);
            return false;
        }

    }

    private void QueueObject(string v, WebhookMessageQueueObj obj, StorageType config, int? visibility_timeout) {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<WebhookMessageLog> SearchExpired() {
        var expireTime = (DateTimeOffset.UtcNow - TimeSpan.FromDays(EXPIRE_DAYS)).ToString("o");

        var timeFilter = $"Timestamp lt datetime'{expireTime}'";
        return QueryAsync(filter: timeFilter);
    }

    public async Async.Task<WebhookMessageLog?> GetWebhookMessageById(Guid webhookId, Guid eventId) {
        var data = QueryAsync(filter: $"PartitionKey eq '{webhookId}' and RowKey eq '{eventId}'");

        return await data.FirstOrDefaultAsync();
    }
}
