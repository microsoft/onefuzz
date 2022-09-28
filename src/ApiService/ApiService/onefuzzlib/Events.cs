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
        private readonly JsonSerializerOptions _options;

        public Events(IQueue queue, IWebhookOperations webhook, ILogTracer log, IContainers containers, ICreds creds) {
            _queue = queue;
            _webhook = webhook;
            _log = log;
            _containers = containers;
            _creds = creds;
            _options = new JsonSerializerOptions(EntityConverter.GetJsonSerializerOptions()) {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            _options.Converters.Add(new RemoveUserInfo());
        }

        public async Async.Task QueueSignalrEvent(EventMessage message) {
            var ev = new SignalREvent("events", new List<EventMessage>() { message });
            await _queue.SendMessage("signalr-events", JsonSerializer.Serialize(ev, _options), StorageType.Config);
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
            var serializedEvent = JsonSerializer.Serialize(anEvent, anEvent.GetType(), _options);
            _log.Info($"sending event: {anEvent.GetEventType():Tag:EventType} - {serializedEvent}");
        }
    }


    public class RemoveUserInfo : JsonConverter<UserInfo> {
        public override UserInfo? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            throw new NotSupportedException("reading UserInfo is not supported");
        }

        public override void Write(Utf8JsonWriter writer, UserInfo value, JsonSerializerOptions options) {
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
    }
}
