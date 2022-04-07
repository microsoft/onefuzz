using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service
{

    public record SignalREvent
    (
        string Targe,
        List<EventMessage> arguments
    );



    public interface IEvents {
        public Task SendEvent(BaseEvent anEvent);

        public Task QueueSignalrEvent(EventMessage message);
    }

    public class Events : IEvents
    {
        private readonly IQueue _queue;
        private readonly ILog _logger;

        public Events(IQueue queue, ILog logger)
        {
            _queue = queue;
            _logger = logger;
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
            //Webhook.send_event(event_message)

            //_logger.LogEvent(Guid.NewGuid(), eventType, new Dictionary<string, string>());
        }
    }
}
