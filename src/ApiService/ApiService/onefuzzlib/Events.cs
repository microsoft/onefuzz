using ApiService.OneFuzzLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service
{

    public record SignalREvent
    (
        string Target,
        List<EventMessage> arguments
    );



    public interface IEvents {
        public Task SendEvent(BaseEvent anEvent);

        public Task QueueSignalrEvent(EventMessage message);
    }

    public class Events : IEvents
    {
        private readonly IQueue _queue;
        private readonly ILogTracer _logger;
        private readonly IWebhookOperations _webhook;

        public Events(IQueue queue, ILogTracer logger, IWebhookOperations webhook)
        {
            _queue = queue;
            _logger = logger;
            _webhook = webhook;
        }

        public async Task QueueSignalrEvent(EventMessage eventMessage)
        {
            var message = new SignalREvent("events", new List<EventMessage>() { eventMessage });
            var encodedMessage = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(message)) ;
            await _queue.SendMessage("signalr-events", encodedMessage, StorageType.Config);
        }

        public async Task SendEvent(BaseEvent anEvent) {
            var eventType = anEvent.GetEventType();

            var eventMessage = new EventMessage(
                Guid.NewGuid(),
                eventType,
                anEvent,
                Guid.NewGuid(), // todo
                "test" //todo
            );
            await QueueSignalrEvent(eventMessage);
            await _webhook.SendEvent(eventMessage);
            LogEvent(anEvent, eventType);
        }

        public void LogEvent(BaseEvent anEvent, EventType eventType)
        {
            //todo
            //var scrubedEvent = FilterEvent(anEvent);
            //throw new NotImplementedException();

        }

        private object FilterEvent(BaseEvent anEvent)
        {
            throw new NotImplementedException();
        }
    }
}
