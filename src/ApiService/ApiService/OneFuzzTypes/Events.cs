using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using System;
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
                EventTaskHeartbeat _ => EventType.TaskHeartbeat,
                EventInstanceConfigUpdated _ => EventType.InstanceConfigUpdated,
                _ => throw new NotImplementedException(),
            };

    }

    public static Type GetTypeInfo(EventType eventType)
    {
        return (eventType) switch
        {
            EventType.NodeHeartbeat => typeof(EventNodeHeartbeat),
            EventType.InstanceConfigUpdated => typeof(EventInstanceConfigUpdated),
            EventType.TaskHeartbeat => typeof(EventTaskHeartbeat),
            _ => throw new ArgumentException($"invalid input {eventType}"),

        };
    }
};

public class EventTypeProvider : ITypeProvider
{
    public Type GetTypeInfo(object input)
    {
        return BaseEvent.GetTypeInfo((input as EventType?) ?? throw new ArgumentException($"input is expected to be an EventType {input}"));
    }
}

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


public record EventTaskHeartbeat(
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


public record EventInstanceConfigUpdated(
    InstanceConfig Config
) : BaseEvent();

public interface IEvent<out T> where T : BaseEvent
{
    public T Event { get; }
}


public record EventMessage(
    Guid EventId,
    EventType EventType,
    [property: TypeDiscrimnatorAttribute("EventType", typeof(EventTypeProvider))]
    [property: JsonConverter(typeof(BaseEventConverter))]
    BaseEvent Event,
    Guid InstanceId,
    String InstanceName
) : EntityBase();

public class BaseEventConverter : JsonConverter<BaseEvent>
{
    public override BaseEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return null;
    }

    public override void Write(Utf8JsonWriter writer, BaseEvent value, JsonSerializerOptions options)
    {
        var eventType = value.GetType();
        JsonSerializer.Serialize(writer, value, eventType, options);
    }
}