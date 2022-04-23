using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using System.Text.Json.Serialization;

namespace Microsoft.OneFuzz.Service;


public enum WebhookMessageFormat
{
    Onefuzz,
    EventGrid
}

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
    [property: JsonPropertyName("eventTime")] DateTimeOffset eventTime,
    Guid Id,
    [property: TypeDiscrimnatorAttribute("EventType", typeof(EventTypeProvider))]
    [property: JsonConverter(typeof(BaseEventConverter))]
    BaseEvent data): EntityBase();


// TODO: This should inherit from Entity Base ? no, since there is
// a table WebhookMessaageLog
public record WebhookMessageLog(
    [RowKey] Guid EventId,
    EventType EventType,
    BaseEvent Event,
    Guid InstanceId,
    String InstanceName,
    [PartitionKey] Guid WebhookId,
    WebhookMessageState State = WebhookMessageState.Queued,
    int TryCount = 0
    ) : WebhookMessage(EventId,
            EventType,
            Event,
            InstanceId,
            InstanceName,
            WebhookId);

public record Webhook(
    [PartitionKey] Guid WebhookId,
    [RowKey] string Name,
    Uri? Url,
    List<EventType> EventTypes,
    string SecretToken, // SecretString??
    WebhookMessageFormat? MessageFormat
    ) : EntityBase();
