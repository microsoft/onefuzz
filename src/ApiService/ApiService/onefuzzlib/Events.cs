using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service {


    public record SignalREvent
    (
        string Target,
        List<EventMessage> arguments
    );


    public interface IEvents {
        Async.Task SendEvent(BaseEvent anEvent);

        Async.Task QueueSignalrEvent(EventMessage message);

        void LogEvent(BaseEvent anEvent);
    }

    public class Events : IEvents {
        private readonly IQueue _queue;
        private readonly IWebhookOperations _webhook;
        private readonly ILogTracer _log;
        private readonly IContainers _containers;
        private readonly ICreds _creds;

        public Events(IQueue queue, IWebhookOperations webhook, ILogTracer log, IContainers containers, ICreds creds) {
            _queue = queue;
            _webhook = webhook;
            _log = log;
            _containers = containers;
            _creds = creds;
        }

        public async Async.Task QueueSignalrEvent(EventMessage message) {
            var ev = new SignalREvent("events", new List<EventMessage>() { message });
            await _queue.SendMessage("signalr-events", JsonSerializer.Serialize(ev), StorageType.Config);
        }

        public async Async.Task SendEvent(BaseEvent anEvent) {
            var eventType = anEvent.GetEventType();

            var instanceId = await _containers.GetInstanceId();

            var eventMessage = new EventMessage(
                Guid.NewGuid(),
                eventType,
                anEvent,
                instanceId,
                _creds.GetInstanceName()
            );
            await QueueSignalrEvent(eventMessage);
            await _webhook.SendEvent(eventMessage);
            LogEvent(anEvent);
        }

        public void LogEvent(BaseEvent anEvent) {
            var options = EntityConverter.GetJsonSerializerOptions();
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.Converters.Add(new RemoveUserInfo());
            var serializedEvent = JsonSerializer.Serialize(anEvent, anEvent.GetType(), options);
            _log.WithTag("Event Type", anEvent.GetEventType().ToString()).Info($"sending event: {anEvent.GetEventType()} - {serializedEvent}");
        }
    }


    public class RemoveUserInfo : JsonConverter<UserInfo> {
        public override UserInfo? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, UserInfo value, JsonSerializerOptions options) {
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
    }
}
