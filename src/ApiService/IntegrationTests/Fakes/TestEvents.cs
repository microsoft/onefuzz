using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;
using Async = System.Threading.Tasks;

namespace IntegrationTests.Fakes;

public sealed class TestEvents : Events {

    public List<BaseEvent> Events { get; } = new();
    public List<DownloadableEventMessage> SignalREvents { get; } = new();

    public TestEvents(ILogger<Events> log, IOnefuzzContext context)
        : base(log, context) { }

    public override void LogEvent(BaseEvent anEvent) {
        Events.Add(anEvent);
    }

    public override Async.Task QueueSignalrEvent(DownloadableEventMessage message) {
        SignalREvents.Add(message);
        return Async.Task.CompletedTask;
    }
}
