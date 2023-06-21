using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;
namespace IntegrationTests.Fakes;

public sealed class TestWebhookOperations : WebhookOperations {

    public List<BaseEvent> Events { get; } = new();
    public List<DownloadableEventMessage> SignalREvents { get; } = new();

    public TestWebhookOperations(IHttpClientFactory httpClientFactory, ILogger<WebhookOperations> log, IOnefuzzContext context)
        : base(httpClientFactory, log, context) { }
}
