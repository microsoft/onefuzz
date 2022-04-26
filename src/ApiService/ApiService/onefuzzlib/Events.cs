﻿using System.Text;
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
        public Async.Task SendEvent(BaseEvent anEvent);

        public Async.Task QueueSignalrEvent(EventMessage message);
    }

    public class Events : IEvents {
        private readonly IQueue _queue;
        private readonly IWebhookOperations _webhook;
        private ILogTracer _log;

        public Events(IQueue queue, IWebhookOperations webhook, ILogTracer log) {
            _queue = queue;
            _webhook = webhook;
            _log = log;
        }

        public async Async.Task QueueSignalrEvent(EventMessage eventMessage) {
            var message = new SignalREvent("events", new List<EventMessage>() { eventMessage });
            var encodedMessage = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
            await _queue.SendMessage("signalr-events", encodedMessage, StorageType.Config);
        }

        public async Async.Task SendEvent(BaseEvent anEvent) {
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

        public void LogEvent(BaseEvent anEvent, EventType eventType) {
            var options = EntityConverter.GetJsonSerializerOptions();
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.Converters.Add(new RemoveUserInfo());
            var serializedEvent = JsonSerializer.Serialize(anEvent, options);
            _log.WithTag("Event Type", eventType.ToString()).Info($"sending event: {eventType} - {serializedEvent}");
        }
    }


    internal class RemoveUserInfo : JsonConverter<UserInfo> {
        public override UserInfo? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            //TODO: I might be wrong but seems like better way of doing this is to have a separate type,
            //that if object of the type - then ignore user info...
            var newOptions = new JsonSerializerOptions(options);
            RemoveUserInfo? self = null;
            foreach (var converter in newOptions.Converters) {
                if (converter is RemoveUserInfo) {
                    self = (RemoveUserInfo)converter;
                    break;
                }
            }

            if (self != null) {
                newOptions.Converters.Remove(self);
            }

            return JsonSerializer.Deserialize<UserInfo>(ref reader, newOptions);
        }

        public override void Write(Utf8JsonWriter writer, UserInfo value, JsonSerializerOptions options) {
            writer.WriteStringValue("{}");
        }
    }
}
