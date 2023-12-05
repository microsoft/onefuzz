using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
namespace Microsoft.OneFuzz.Service {


    public record SignalREvent
    (
        string Target,
        List<DownloadableEventMessage> arguments
    ) : ITruncatable<SignalREvent> {
        public SignalREvent Truncate(int maxLength) {
            return this with {
                arguments = arguments.Select(x => x.Truncate(maxLength)).ToList()
            };
        }
    }

    public interface IEvents {
        Async.Task SendEvent(BaseEvent anEvent);
        Async.Task QueueSignalrEvent(DownloadableEventMessage message);

        void LogEvent(BaseEvent anEvent);
        Async.Task<OneFuzzResult<DownloadableEventMessage>> GetDownloadableEvent(Guid eventId);
        Async.Task<DownloadableEventMessage> MakeDownloadable(EventMessage eventMessage);
    }

    public class Events : IEvents {
        private readonly IQueue _queue;
        private readonly IWebhookOperations _webhook;
        private readonly ILogger _log;
        private readonly IContainers _containers;
        private readonly ICreds _creds;
        private readonly JsonSerializerOptions _options;
        private readonly JsonSerializerOptions _optionsSlim;
        private readonly JsonSerializerOptions _deserializingFromBlobOptions;
        private readonly IOnefuzzContext _context;

        public Events(ILogger<Events> log, IOnefuzzContext context) {
            _queue = context.Queue;
            _webhook = context.WebhookOperations;
            _log = log;
            _containers = context.Containers;
            _creds = context.Creds;
            _options = new JsonSerializerOptions(EntityConverter.GetJsonSerializerOptions()) {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            _options.Converters.Add(new RemoveUserInfo());
            _optionsSlim = new JsonSerializerOptions(_options);
            _optionsSlim.Converters.Add(new EventExportConverter<DownloadableEventMessage>());
            _deserializingFromBlobOptions = new JsonSerializerOptions(EntityConverter.GetJsonSerializerOptions()) {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            _context = context;
        }

        public virtual async Async.Task QueueSignalrEvent(DownloadableEventMessage message) {
            var tags = new (string, string)[] {
                ("event_type", message.EventType.ToString()),
                ("event_id", message.EventId.ToString())
            };
            var ev = new SignalREvent("events", new List<DownloadableEventMessage>() { message });

            var opts = await _context.FeatureManagerSnapshot.IsEnabledAsync(FeatureFlagConstants.EnableSlimEventSerialization) switch {
                true => _optionsSlim,
                false => _options
            };

            var queueResult = await _queue.QueueObject("signalr-events", ev, StorageType.Config, serializerOptions: opts);

            if (!queueResult) {
                _log.AddTags(tags);
                _log.LogError("Failed to queue signalr event");
            }
        }

        public async Async.Task SendEvent(BaseEvent anEvent) {
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

        public virtual void LogEvent(BaseEvent anEvent) {
            var serializedEvent = JsonSerializer.Serialize(anEvent, anEvent.GetType(), _options);
            _log.LogInformation("logging event: {EventType} - {serializedEvent}", anEvent.GetEventType(), serializedEvent);
        }

        public async Async.Task<OneFuzzResult<DownloadableEventMessage>> GetDownloadableEvent(Guid eventId) {
            var (data, tags) = await _containers.GetBlobWithTags(WellKnownContainers.Events, eventId.ToString(), StorageType.Corpus);
            if (data == null) {
                return OneFuzzResult<DownloadableEventMessage>.Error(ErrorCode.UNABLE_TO_FIND, $"Could not find container for event with id {eventId}");
            }

            var eventMessage = JsonSerializer.Deserialize<EventMessage>(data, _deserializingFromBlobOptions);
            if (eventMessage == null) {
                return OneFuzzResult<DownloadableEventMessage>.Error(ErrorCode.UNEXPECTED_DATA_SHAPE, $"Could not deserialize event with id {eventId}");
            }

            var sasUrl = await _containers.GetFileSasUrl(
                WellKnownContainers.Events,
                eventId.ToString(),
                StorageType.Corpus,
                BlobSasPermissions.Read);

            if (sasUrl == null) {
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

        public async Task<DownloadableEventMessage> MakeDownloadable(EventMessage eventMessage) {
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
}
