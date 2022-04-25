using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using ApiService.OneFuzzLib.Orm;
using System.Text.Json;
using System.Security.Cryptography;
using System.Net;
using System.Net.Http;

namespace Microsoft.OneFuzz.Service;

public interface IWebhookOperations
{
    Async.Task SendEvent(EventMessage eventMessage);
    Async.Task<Webhook?> GetByWebhookId(Guid webhookId);
    Async.Task<bool> Send(WebhookMessageLog messageLog);
}

public class WebhookOperations : Orm<Webhook>, IWebhookOperations
{

    // Needs to eventually pull from global __version__
    const string USER_AGENT = "onefuzz-webhook 0.0.0";
    private readonly IWebhookMessageLogOperations _webhookMessageLogOperations;
    private readonly ILogTracer _log;
    private readonly ICreds _creds;
    private readonly IContainers _containers;
    private readonly IHttpClientFactory _httpFactory;

    public WebhookOperations(IHttpClientFactory httpFactory, ICreds creds, IStorage storage, IWebhookMessageLogOperations webhookMessageLogOperations, IContainers containers, ILogTracer log, IServiceConfig config)
        : base(storage, log, config)
    {
        _webhookMessageLogOperations = webhookMessageLogOperations;
        _log = log;
        _creds = creds;
        _containers = containers;
        _httpFactory = httpFactory;
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

    public async Async.Task<bool> Send(WebhookMessageLog messageLog)
    {
        var webhook = await GetByWebhookId(messageLog.WebhookId);
        if (webhook == null || webhook.Url == null)
        {
            throw new Exception($"Invalid Webhook. Webhook with WebhookId: {messageLog.WebhookId} Not Found");
        }

        var (data, digest) = await BuildMessage(webhookId: webhook.WebhookId, eventId: messageLog.EventId, eventType: messageLog.EventType, webhookEvent: messageLog.Event, secretToken: webhook.SecretToken, messageFormat: webhook.MessageFormat);

        var headers = new Dictionary<string, string> { { "Content-type", "application/json" }, { "User-Agent", USER_AGENT } };

        if (digest != null)
        {
            headers["X-Onefuzz-Digest"] = digest;
        }

        var client = new Request(_httpFactory.CreateClient());
        var response = client.Post(url: webhook.Url, json: data, headers: headers);
        var result = response.Result;
        if (result.StatusCode == HttpStatusCode.Accepted)
        {
            return true;
        }
        return false;
    }

    // Not converting to bytes, as it's not neccessary in C#. Just keeping as string. 
    public async Async.Task<Tuple<string, string?>> BuildMessage(Guid webhookId, Guid eventId, EventType eventType, BaseEvent webhookEvent, String? secretToken, WebhookMessageFormat? messageFormat)
    {
        var entityConverter = new EntityConverter();
        string data = "";
        if (messageFormat != null && messageFormat == WebhookMessageFormat.EventGrid)
        {
            var eventGridMessage = new[] { new WebhookMessageEventGrid(Id: eventId, Data: webhookEvent, DataVersion: "1.0.0", Subject: _creds.GetInstanceName(), EventType: eventType, EventTime: DateTimeOffset.UtcNow) };
            data = JsonSerializer.Serialize(eventGridMessage, options: EntityConverter.GetJsonSerializerOptions());
        }
        else
        {
            var instanceId = await _containers.GetInstanceId();
            var webhookMessage = new WebhookMessage(WebhookId: webhookId, EventId: eventId, EventType: eventType, Event: webhookEvent, InstanceId: instanceId, InstanceName: _creds.GetInstanceName());
            data = JsonSerializer.Serialize(webhookMessage, options: EntityConverter.GetJsonSerializerOptions());
        }

        string? digest = null;
        var hmac = HMAC.Create("HMACSHA512");
        if (secretToken != null && hmac != null)
        {
            hmac.Key = System.Text.Encoding.UTF8.GetBytes(secretToken);
            digest = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data)));
        }

        return new Tuple<string, string?>(data, digest);

    }

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
