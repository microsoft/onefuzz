using System.Collections.Generic;
using Microsoft.OneFuzz.Service;

using Async = System.Threading.Tasks;

namespace IntegrationTests.Fakes;

public sealed class TestEvents : IEvents {

    public List<BaseEvent> Events { get; } = new();
    public List<EventMessage> SignalREvents { get; } = new();

    public void LogEvent(BaseEvent anEvent) {
        Events.Add(anEvent);
    }

    public Async.Task QueueSignalrEvent(EventMessage message) {
        SignalREvents.Add(message);
        return Async.Task.CompletedTask;
    }

    public Async.Task SendEvent(BaseEvent anEvent) {
        Events.Add(anEvent);
        return Async.Task.CompletedTask;
    }
}
