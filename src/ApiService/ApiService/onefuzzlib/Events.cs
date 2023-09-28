using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
namespace Microsoft.OneFuzz.Service
{


    public record SignalREvent
    (
        string Target,
        List<DownloadableEventMessage> arguments
    ) : ITruncatable<SignalREvent>
    {
        public SignalREvent Truncate(int maxLength)
        {
            return this with
            {
                arguments = arguments.Select(x => x.Truncate(maxLength)).ToList()
            };
        }
    }

    public interface IEvents
    {
        Async.Task SendEvent(BaseEvent anEvent);
        Async.Task QueueSignalrEvent(DownloadableEventMessage message);

        void LogEvent(BaseEvent anEvent);
        Async.Task<OneFuzzResult<DownloadableEventMessage>> GetDownloadableEvent(Guid eventId);
        Async.Task<DownloadableEventMessage> MakeDownloadable(EventMessage eventMessage);
    }

    public class Events : IEvents
    {
        private readonly IQueue _queue;
        private readonly IWebhookOperations _webhook;
        private readonly ILogger _log;
        private readonly IContainers _containers;
        private readonly ICreds _creds;
        private readonly JsonSerializerOptions _options;
        private readonly JsonSerializerOptions _deserializingFromBlobOptions;

        public Events(ILogger<Events> log, IOnefuzzContext context)
        {
            _queue = context.Queue;
            _webhook = context.WebhookOperations;
            _log = log;
            _containers = context.Containers;
            _creds = context.Creds;
            _options = new JsonSerializerOptions(EntityConverter.GetJsonSerializerOptions())
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            _options.Converters.Add(new RemoveUserInfo());
            _deserializingFromBlobOptions = new JsonSerializerOptions(EntityConverter.GetJsonSerializerOptions())
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        public virtual async Async.Task QueueSignalrEvent(DownloadableEventMessage message)
        {
            var tags = new (string, string)[] {
                ("event_type", message.EventType.ToString()),
                ("event_id", message.EventId.ToString())
            };
            var ev = new SignalREvent("events", new List<DownloadableEventMessage>() { message });
            var queueResult = await _queue.QueueObject("signalr-events", ev, StorageType.Config, serializerOptions: _options);

            if (!queueResult)
            {
                _log.AddTags(tags);
                _log.LogError("Failed to queue signalr event");
            }
        }

        public async Async.Task SendEvent(BaseEvent anEvent)
        {
            var eventType = anEvent.GetEventType();

            var instanceId = await _containers.GetInstanceId();
            var creationDate = DateTime.UtcNow;
            var eventMessage = new EventMessage(
                Guid.NewGuid(),
                eventType,
                anEvent,
                instanceId,
                _creds.GetInstanceName(),
                creationDate
            );

            var downloadableEventMessage = await MakeDownloadable(eventMessage);

            await QueueSignalrEvent(downloadableEventMessage);
            await _webhook.SendEvent(downloadableEventMessage);
            LogEvent(anEvent);
        }

        public virtual void LogEvent(BaseEvent anEvent)
        {
            var serializedEvent = JsonSerializer.Serialize(anEvent, anEvent.GetType(), _options);
            _log.LogInformation("sending event: {EventType} - {serializedEvent}", anEvent.GetEventType(), serializedEvent);
        }

        public async Async.Task<OneFuzzResult<DownloadableEventMessage>> GetDownloadableEvent(Guid eventId)
        {
            var (data, tags) = await _containers.GetBlobWithTags(WellKnownContainers.Events, eventId.ToString(), StorageType.Corpus);
            if (data == null)
            {
                return OneFuzzResult<DownloadableEventMessage>.Error(ErrorCode.UNABLE_TO_FIND, $"Could not find container for event with id {eventId}");
            }

            var eventMessage = JsonSerializer.Deserialize<EventMessage>(data, _deserializingFromBlobOptions);
            if (eventMessage == null)
            {
                return OneFuzzResult<DownloadableEventMessage>.Error(ErrorCode.UNEXPECTED_DATA_SHAPE, $"Could not deserialize event with id {eventId}");
            }

            var sasUrl = await _containers.GetFileSasUrl(
                WellKnownContainers.Events,
                eventId.ToString(),
                StorageType.Corpus,
                BlobSasPermissions.Read);

            if (sasUrl == null)
            {
                return OneFuzzResult<DownloadableEventMessage>.Error(
                    ErrorCode.UNABLE_TO_FIND,
                    $"Could not find container for event with id {eventId}");
            }

            return OneFuzzResult<DownloadableEventMessage>.Ok(new DownloadableEventMessage(
                eventMessage.EventId,
                eventMessage.EventType,
                eventMessage.Event,
                eventMessage.InstanceId,
                eventMessage.InstanceName,
                eventMessage.CreatedAt,
                sasUrl,
                RetentionPolicyUtils.GetExpiryDateTagFromTags(tags)
            ));
        }

        public async Task<DownloadableEventMessage> MakeDownloadable(EventMessage eventMessage)
        {
            await _containers.SaveBlob(
                WellKnownContainers.Events,
                eventMessage.EventId.ToString(),
                JsonSerializer.Serialize(eventMessage, _options),
                StorageType.Corpus,
                expiresOn: eventMessage.GetExpiryDate());

            var sasUrl = await _containers.GetFileSasUrl(
                WellKnownContainers.Events,
                eventMessage.EventId.ToString(),
                StorageType.Corpus,
                BlobSasPermissions.Read)
                // events container should always exist
                ?? throw new InvalidOperationException("Events container is missing");

            return new DownloadableEventMessage(
                eventMessage.EventId,
                eventMessage.EventType,
                eventMessage.Event,
                eventMessage.InstanceId,
                eventMessage.InstanceName,
                eventMessage.CreatedAt,
                sasUrl,
                eventMessage.GetExpiryDate()
            );
        }
    }


    public class RemoveUserInfo : JsonConverter<UserInfo>
    {
        public override UserInfo? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("reading UserInfo is not supported");
        }

        public override void Write(Utf8JsonWriter writer, UserInfo value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// <b>THIS IS A WRITE ONLY JSON CONVERTER</b>
    /// <br/>
    /// It should only be used when serializing event messages to send via queue/webhooks
    /// </summary>
    public class EventExportConverter : JsonConverter<DownloadableEventMessage>
    {
        private static HashSet<Type> boundedTypes = new HashSet<Type>{
            typeof(Guid),
            typeof(DateTime),
            typeof(Enum),
            typeof(int),
            typeof(bool),
            typeof(float),
            typeof(double),
            typeof(long),
            typeof(char),
        };

        public override DownloadableEventMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("This converter should only be used when serializing event messages to send via queue/webhooks");
        }

        public override void Write(Utf8JsonWriter writer, DownloadableEventMessage value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            var properties = value.GetType().GetProperties();
            var nonNullProperties = properties.Where(p => p.GetValue(value, null) != null)
                .Where(p => HasBoundedSerialization(p));
            writer.WriteEndObject();
        }

        public static bool HasBoundedSerialization(PropertyInfo propertyInfo)
        {
            return boundedTypes.Contains(propertyInfo.PropertyType) ||
                propertyInfo.GetType().GetInterfaces()
                    .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidatedString<>));
        }

    }
}
