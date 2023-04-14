﻿using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Azure.Storage.Sas;

namespace Microsoft.OneFuzz.Service {


    public record SignalREvent
    (
        string Target,
        List<DownloadableEventMessage> arguments
    );


    public interface IEvents {
        Async.Task SendEvent(BaseEvent anEvent);

        Async.Task QueueSignalrEvent(DownloadableEventMessage message);

        void LogEvent(BaseEvent anEvent);

        Async.Task<OneFuzzResult<EventMessage>> GetEvent(Guid eventId);
        Async.Task<OneFuzzResult<DownloadableEventMessage>> GetDownloadableEvent(Guid eventId);
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

        public async Async.Task QueueSignalrEvent(DownloadableEventMessage message) {
            var ev = new SignalREvent("events", new List<DownloadableEventMessage>() { message });
            await _queue.SendMessage("signalr-events", JsonSerializer.Serialize(ev, _options), StorageType.Config);
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

            await _containers.SaveBlob(WellKnownContainers.Events, eventMessage.EventId.ToString(), JsonSerializer.Serialize(eventMessage, _options), StorageType.Corpus);
            var sasUrl = await _containers.GetFileSasUrl(WellKnownContainers.Events, eventMessage.EventId.ToString(), StorageType.Corpus, BlobSasPermissions.Read);

            var downloadableEventMessage = new DownloadableEventMessage(
                eventMessage.EventId,
                eventType,
                anEvent,
                instanceId,
                _creds.GetInstanceName(),
                creationDate,
                sasUrl
            );

            await QueueSignalrEvent(downloadableEventMessage);
            await _webhook.SendEvent(downloadableEventMessage);
            LogEvent(anEvent);
        }

        public void LogEvent(BaseEvent anEvent) {
            var serializedEvent = JsonSerializer.Serialize(anEvent, anEvent.GetType(), _options);
            _log.Info($"sending event: {anEvent.GetEventType():Tag:EventType} - {serializedEvent}");
        }

        public async Async.Task<OneFuzzResult<EventMessage>> GetEvent(Guid eventId) {
            var blob = await _containers.GetBlob(WellKnownContainers.Events, eventId.ToString(), StorageType.Corpus);
            if (blob == null) {
                return OneFuzzResult<EventMessage>.Error(ErrorCode.UNABLE_TO_FIND, $"Could not find container for event with id {eventId}");
            }

            var eventMessage = JsonSerializer.Deserialize<EventMessage>(blob, _options);
            if (eventMessage == null) {
                return OneFuzzResult<EventMessage>.Error(ErrorCode.UNEXPECTED_DATA_SHAPE, $"Could not deserialize event with id {eventId}");
            }

            return OneFuzzResult<EventMessage>.Ok(eventMessage);
        }

        public async Async.Task<OneFuzzResult<DownloadableEventMessage>> GetDownloadableEvent(Guid eventId) {
            var eventMessageResult = await GetEvent(eventId);
            if (!eventMessageResult.IsOk) {
                return OneFuzzResult<DownloadableEventMessage>.Error(eventMessageResult.ErrorV);
            }

            var sasUrl = await _containers.GetFileSasUrl(WellKnownContainers.Events, eventId.ToString(), StorageType.Corpus, BlobSasPermissions.Read);
            if (sasUrl == null) {
                return OneFuzzResult<DownloadableEventMessage>.Error(ErrorCode.UNABLE_TO_FIND, $"Could not find container for event with id {eventId}");
            }

            var eventMessage = eventMessageResult.OkV!;

            return OneFuzzResult<DownloadableEventMessage>.Ok(new DownloadableEventMessage(
                eventMessage.EventId,
                eventMessage.EventType,
                eventMessage.Event,
                eventMessage.InstanceId,
                eventMessage.InstanceName,
                eventMessage.CreatedAt,
                sasUrl
            ));
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
