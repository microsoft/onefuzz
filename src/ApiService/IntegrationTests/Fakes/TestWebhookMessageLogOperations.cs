using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;
namespace IntegrationTests.Fakes;

public sealed class TestWebhookMessageLogOperations : WebhookMessageLogOperations {

    public List<BaseEvent> Events { get; } = new();
    public List<DownloadableEventMessage> SignalREvents { get; } = new();

    public TestWebhookMessageLogOperations(ILogger<WebhookMessageLogOperations> log, IOnefuzzContext context)
        : base(log, context) { }
}
