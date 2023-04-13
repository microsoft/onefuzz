﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.OneFuzz.Service;

using Async = System.Threading.Tasks;

namespace IntegrationTests.Fakes;

public sealed class TestEvents : IEvents {

    public List<BaseEvent> Events { get; } = new();
    public List<DownloadableEventMessage> SignalREvents { get; } = new();

    public Task<DownloadableEventMessage> GetDownloadableEvent(Guid eventId) {
        throw new NotImplementedException();
    }

    public Task<EventMessage> GetEvent(Guid eventId) {
        throw new NotImplementedException();
    }

    public void LogEvent(BaseEvent anEvent) {
        Events.Add(anEvent);
    }

    public Async.Task QueueSignalrEvent(DownloadableEventMessage message) {
        SignalREvents.Add(message);
        return Async.Task.CompletedTask;
    }

    public Async.Task SendEvent(BaseEvent anEvent) {
        Events.Add(anEvent);
        return Async.Task.CompletedTask;
    }
}
