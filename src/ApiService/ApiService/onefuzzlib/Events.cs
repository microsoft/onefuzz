using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Azure.Storage.Sas;

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

        Async.Task<BaseEvent> GetEvent(Guid eventId);
    }

    public class Events : IEvents {
        private readonly IQueue _queue;
        private readonly IWebhookOperations _webhook;
        private readonly ILogTracer _log;
        private readonly IContainers _containers;
        private readonly ICreds _creds;
        private readonly JsonSerializerOptions _options;

        public Events(ILogTracer log, IOnefuzzContext context) {
            _queue = context.Queue;
            _webhook = context.WebhookOperations;
            _log = log;
            _containers = context.Containers;
            _creds = context.Creds;
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
                _creds.GetInstanceName(),
                DateTime.UtcNow,
                new Uri("http://example.com")
            );

            var container = Container.Parse("events");
            _ = await _containers.CreateContainer(container, StorageType.Corpus, null);
            await _containers.SaveBlob(container, eventMessage.EventId.ToString(), JsonSerializer.Serialize(eventMessage, _options), StorageType.Corpus);
            var sasUrl = await _containers.GetFileSasUrl(container, eventMessage.EventId.ToString(), StorageType.Corpus, BlobSasPermissions.Read);

            await QueueSignalrEvent(eventMessage);
            await _webhook.SendEvent(eventMessage);
            LogEvent(anEvent);
        }

        public void LogEvent(BaseEvent anEvent) {
            var serializedEvent = JsonSerializer.Serialize(anEvent, anEvent.GetType(), _options);
            _log.Info($"sending event: {anEvent.GetEventType():Tag:EventType} - {serializedEvent}");
        }

        public Async.Task<BaseEvent> GetEvent(Guid eventId) {
            // TODO: Load the event data from container and include a read only sas url
            throw new NotImplementedException();
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
