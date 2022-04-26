using System.Text.Json.Serialization;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;


public enum WebhookMessageFormat {
    Onefuzz,
    EventGrid
}

public record WebhookMessageQueueObj(
        Guid WebhookId,
        Guid EventId
        );

public record WebhookMessage(Guid EventId,
    EventType EventType,
    BaseEvent Event,
    Guid InstanceId,
    String InstanceName,
    Guid WebhookId) : EventMessage(EventId, EventType, Event, InstanceId, InstanceName);


public record WebhookMessageEventGrid(
    [property: JsonPropertyName("dataVersion")] string DataVersion,
    string Subject,
    [property: JsonPropertyName("EventType")] EventType EventType,
    [property: JsonPropertyName("eventTime")] DateTimeOffset EventTime,
    Guid Id,
    [property: TypeDiscrimnatorAttribute("EventType", typeof(EventTypeProvider))]
    [property: JsonConverter(typeof(BaseEventConverter))]
    BaseEvent Data);

public record WebhookMessageLog(
    [RowKey] Guid EventId,
    EventType EventType,
    [property: TypeDiscrimnatorAttribute("EventType", typeof(EventTypeProvider))]
    [property: JsonConverter(typeof(BaseEventConverter))]
    BaseEvent Event,
    Guid InstanceId,
    String InstanceName,
    [PartitionKey] Guid WebhookId,
    long TryCount,
    WebhookMessageState State = WebhookMessageState.Queued
    ) : EntityBase();

public record Webhook(
    [PartitionKey] Guid WebhookId,
    [RowKey] string Name,
    Uri? Url,
    List<EventType> EventTypes,
    string SecretToken, // SecretString??
    WebhookMessageFormat? MessageFormat
    ) : EntityBase();
