using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PoolName = System.String;

namespace Microsoft.OneFuzz.Service;




public enum EventType
{
    JobCreated,
    JobStopped,
    NodeCreated,
    NodeDeleted,
    NodeStateUpdated,
    Ping,
    PoolCreated,
    PoolDeleted,
    ProxyCreated,
    ProxyDeleted,
    ProxyFailed,
    ProxyStateUpdated,
    ScalesetCreated,
    ScalesetDeleted,
    ScalesetFailed,
    ScalesetStateUpdated,
    ScalesetResizeScheduled,
    TaskCreated,
    TaskFailed,
    TaskStateUpdated,
    TaskStopped,
    CrashReported,
    RegressionReported,
    FileAdded,
    TaskHeartbeat,
    NodeHeartbeat,
    InstanceConfigUpdated
}

public abstract record BaseEvent()
{

    public EventType GetEventType()
    {
        return
            this switch
            {
                EventNodeHeartbeat _ => EventType.NodeHeartbeat,
                EventInstanceConfigUpdated _ => EventType.InstanceConfigUpdated,
                _ => throw new NotImplementedException(),
            };

    }
};

//public record EventTaskStopped(
//    Guid JobId,
//    Guid TaskId,
//    UserInfo? UserInfo,
//    TaskConfig Config
//) : BaseEvent();


//record EventTaskFailed(
//    Guid JobId,
//    Guid TaskId,
//    Error Error,
//    UserInfo? UserInfo,
//    TaskConfig Config
//    ) : BaseEvent();


//record EventJobCreated(
//    Guid JobId,
//    JobConfig Config,
//    UserInfo? UserInfo
//    ) : BaseEvent();


//record JobTaskStopped(
//    Guid TaskId,
//    TaskType TaskType,
//    Error? Error
//    ) : BaseEvent();

//record EventJobStopped(
//    Guid JobId: UUId,
//    JobConfig Config,
//    UserInfo? UserInfo,
//    List<JobTaskStopped> TaskInfo
//): BaseEvent();


//record EventTaskCreated(
//    Guid JobId,
//    Guid TaskId,
//    TaskConfig Config,
//    UserInfo? UserInfo
//    ) : BaseEvent();


//record EventTaskStateUpdated(
//    Guid JobId,
//    Guid TaskId,
//    TaskState State,
//    DateTimeOffset? EndTime,
//    TaskConfig Config
//    ) : BaseEvent();


record EventTaskHeartbeat(
   Guid JobId,
   Guid TaskId,
   TaskConfig Config
) : BaseEvent();


//record EventPing(
//    PingId: Guid
//): BaseEvent();


//record EventScalesetCreated(
//    Guid ScalesetId,
//    PoolName PoolName,
//    string VmSku,
//    string Image,
//    Region Region,
//    int Size) : BaseEvent();


//record EventScalesetFailed(
//    Guid ScalesetId,
//    PoolName: PoolName,
//    Error: Error
//): BaseEvent();


//record EventScalesetDeleted(
//    Guid ScalesetId,
//    PoolName PoolName,

//    ) : BaseEvent();


//record EventScalesetResizeScheduled(
//    Guid ScalesetId,
//    PoolName PoolName,
//    int size
//    ) : BaseEvent();


//record EventPoolDeleted(
//    PoolName PoolName
//    ) : BaseEvent();


//record EventPoolCreated(
//    PoolName PoolName,
//    Os Os,
//    Architecture Arch,
//    bool Managed,
//    AutoScaleConfig? Autoscale
//    ) : BaseEvent();


//record EventProxyCreated(
//    Region Region,
//    Guid? ProxyId,

//    ) : BaseEvent();


//record EventProxyDeleted(
//    Region Region,
//    Guid? ProxyId
//) : BaseEvent();


//record EventProxyFailed(
//    Region Region,
//    Guid? ProxyId,
//    Error Error
//) : BaseEvent();


//record EventProxyStateUpdated(
//    Region Region,
//    Guid ProxyId,
//    VmState State
//    ) : BaseEvent();


//record EventNodeCreated(
//    Guid MachineId,
//    Guid? ScalesetId,
//    PoolName PoolName
//    ) : BaseEvent();



public record EventNodeHeartbeat(
    Guid MachineId,
    Guid? ScalesetId,
    PoolName PoolName
    ) : BaseEvent();


//    record EventNodeDeleted(
//        Guid MachineId,
//        Guid ScalesetId,
//        PoolName PoolName
//        ) : BaseEvent();


//    record EventScalesetStateUpdated(
//        Guid ScalesetId,
//        PoolName PoolName,
//        ScalesetState State
//        ) : BaseEvent();

//    record EventNodeStateUpdated(
//        Guid MachineId,
//        Guid? ScalesetId,
//        PoolName PoolName,
//        NodeState state
//        ) : BaseEvent();

//    record EventCrashReported(
//        Report Report,
//        Container Container,
//        [property: JsonPropertyName("filename")] String FileName,
//        TaskConfig? TaskConfig
//        ) : BaseEvent();

//    record EventRegressionReported(
//        RegressionReport RegressionReport,
//        Container Container,
//        [property: JsonPropertyName("filename")] String FileName,
//        TaskConfig? TaskConfig
//        ) : BaseEvent();


//    record EventFileAdded(
//        Container Container,
//        [property: JsonPropertyName("filename")] String FileName
//        ) : BaseEvent();


record EventInstanceConfigUpdated(
    InstanceConfig Config
) : BaseEvent();


[JsonConverter(typeof(EventConverter))]
public record EventMessage(
    Guid EventId,
    EventType EventType,
    BaseEvent Event,
    Guid InstanceId,
    String InstanceName
) : EntityBase();

public class EventConverter : JsonConverter<EventMessage>
{

    private readonly Dictionary<string, Type> _allBaseEvents;

    public EventConverter()
    {
        _allBaseEvents = typeof(BaseEvent).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(BaseEvent))).ToDictionary(x => x.Name);
    }


    public override EventMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {


        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException();
        }

        using (var jsonDocument = JsonDocument.ParseValue(ref reader))
        {
            if (!jsonDocument.RootElement.TryGetProperty("event_type", out var eventType))
            {
                throw new JsonException();
            }
            if (!jsonDocument.RootElement.TryGetProperty("event", out var eventData))
            {
                throw new JsonException();
            }
            if (!jsonDocument.RootElement.TryGetProperty("event_id", out var eventId))
            {
                throw new JsonException();
            }
            if (!jsonDocument.RootElement.TryGetProperty("instance_id", out var instanceId))
            {
                throw new JsonException();
            }
            if (!jsonDocument.RootElement.TryGetProperty("instance_name", out var instanceName))
            {
                throw new JsonException();
            }


            var eventTypeName = eventType.GetString() ?? throw new JsonException();

            if (!_allBaseEvents.TryGetValue($"Event{CaseConverter.SnakeToPascal(eventTypeName)}", out var eType))
            {
                throw new JsonException($"Unknown eventType {eventTypeName}");
            }

            var rawTest = eventData.GetRawText();
            var eventClass = (BaseEvent)(JsonSerializer.Deserialize(eventData.GetRawText(), eType, options: options) ?? throw new JsonException());
            var eventTypeEnum = Enum.Parse<EventType>(CaseConverter.SnakeToPascal(eventTypeName));


            return new EventMessage(
                eventId.GetGuid(),
                eventTypeEnum,
                eventClass,
                instanceId.GetGuid(),
                instanceName.GetString() ?? throw new JsonException()
            );
        }
    }

    public override void Write(Utf8JsonWriter writer, EventMessage value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("event_id", value.EventId.ToString());
        writer.WriteString("event_type", CaseConverter.PascalToSnake(value.EventType.ToString()));
        writer.WritePropertyName("event");
        var eventstr = JsonSerializer.Serialize(value.Event, value.Event.GetType(), options);
        writer.WriteRawValue(eventstr);
        writer.WriteString("instance_id", value.InstanceId.ToString());
        writer.WriteString("instance_name", value.InstanceName);
        writer.WriteEndObject();
    }
}
