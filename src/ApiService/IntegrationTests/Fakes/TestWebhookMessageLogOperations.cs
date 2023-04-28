using System.Collections.Generic;
using Microsoft.OneFuzz.Service;

namespace IntegrationTests.Fakes;

public sealed class TestWebhookMessageLogOperations : WebhookMessageLogOperations {

    public List<BaseEvent> Events { get; } = new();
    public List<DownloadableEventMessage> SignalREvents { get; } = new();

    public TestWebhookMessageLogOperations(ILogTracer log, IOnefuzzContext context)
        : base(log, context) { }
}
