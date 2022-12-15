using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

/// <summary>
/// Identifies the enum type associated with the event class
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class EventTypeAttribute : Attribute {
    public EventTypeAttribute(EventType eventType) {
        this.EventType = eventType;
    }

    public EventType EventType { get; }
}


public enum EventType {
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
    InstanceConfigUpdated,
}

public abstract record BaseEvent() {

    private static readonly IReadOnlyDictionary<Type, EventType> typeToEvent;
    private static readonly IReadOnlyDictionary<EventType, Type> eventToType;

    static BaseEvent() {

        EventType ExtractEventType(Type type) {
            var attr = type.GetCustomAttribute<EventTypeAttribute>();
            if (attr is null) {
                throw new InvalidOperationException($"Type {type} is missing {nameof(EventTypeAttribute)}");
            }
            return attr.EventType;
        }

        typeToEvent =
            typeof(BaseEvent).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(BaseEvent)))
            .ToDictionary(x => x, ExtractEventType);

        eventToType = typeToEvent.ToDictionary(x => x.Value, x => x.Key);

        // check that all event types are accounted for
        var allEventTypes = Enum.GetValues<EventType>();
        var missingEventTypes = allEventTypes.Except(eventToType.Keys).ToList();
        if (missingEventTypes.Any()) {
            throw new InvalidOperationException($"Missing event types: {string.Join(", ", missingEventTypes)}");
        }
    }


    public EventType GetEventType() {
        var type = this.GetType();
        if (typeToEvent.TryGetValue(type, out var eventType)) {
            return eventType;
        }

        throw new NotSupportedException($"Unknown event type: {type.GetType()}");
    }

    public static Type GetTypeInfo(EventType eventType) {
        if (eventToType.TryGetValue(eventType, out var type)) {
            return type;
        }

        throw new ArgumentException($"Unknown event type: {eventType}");
    }
};

public class EventTypeProvider : ITypeProvider {
    public Type GetTypeInfo(object input) {
        return BaseEvent.GetTypeInfo((input as EventType?) ?? throw new ArgumentException($"input is expected to be an EventType {input}"));
    }
}

[EventType(EventType.TaskStopped)]
public record EventTaskStopped(
    Guid JobId,
    Guid TaskId,
    UserInfo? UserInfo,
    TaskConfig Config
) : BaseEvent();

[EventType(EventType.TaskFailed)]
public record EventTaskFailed(
    Guid JobId,
    Guid TaskId,
    Error Error,
    UserInfo? UserInfo,
    TaskConfig Config
    ) : BaseEvent();


[EventType(EventType.JobCreated)]
public record EventJobCreated(
   Guid JobId,
   JobConfig Config,
   UserInfo? UserInfo
   ) : BaseEvent();


[EventType(EventType.JobTaskStopped)]
public record EventJobTaskStopped(
    Guid TaskId,
    TaskType TaskType,
    Error? Error
    );


[EventType(EventType.JobStopped)]
public record EventJobStopped(
    Guid JobId,
    JobConfig Config,
    UserInfo? UserInfo,
    List<JobTaskStopped> TaskInfo
) : BaseEvent();


[EventType(EventType.TaskCreated)]
public record EventTaskCreated(
    Guid JobId,
    Guid TaskId,
    TaskConfig Config,
    UserInfo? UserInfo
    ) : BaseEvent();

[EventType(EventType.TaskStateUpdated)]
public record EventTaskStateUpdated(
    Guid JobId,
    Guid TaskId,
    TaskState State,
    DateTimeOffset? EndTime,
    TaskConfig Config
    ) : BaseEvent();


[EventType(EventType.TaskHeartbeat)]
public record EventTaskHeartbeat(
   Guid JobId,
   Guid TaskId,
   TaskConfig Config
) : BaseEvent();

[EventType(EventType.Ping)]
public record EventPing(
    Guid PingId
) : BaseEvent();


[EventType(EventType.ScalesetCreated)]
public record EventScalesetCreated(
   Guid ScalesetId,
   PoolName PoolName,
   string VmSku,
   string Image,
   Region Region,
   int Size) : BaseEvent();


[EventType(EventType.ScalesetFailed)]
public record EventScalesetFailed(
    Guid ScalesetId,
    PoolName PoolName,
    Error Error
) : BaseEvent();


[EventType(EventType.ScalesetDeleted)]
public record EventScalesetDeleted(
   Guid ScalesetId,
   PoolName PoolName

   ) : BaseEvent();


[EventType(EventType.ScalesetResizeScheduled)]
public record EventScalesetResizeScheduled(
    Guid ScalesetId,
    PoolName PoolName,
    long size
    ) : BaseEvent();


[EventType(EventType.PoolDeleted)]
public record EventPoolDeleted(
   PoolName PoolName
   ) : BaseEvent();


[EventType(EventType.PoolCreated)]
public record EventPoolCreated(
   PoolName PoolName,
   Os Os,
   Architecture Arch,
   bool Managed
   // ignoring AutoScaleConfig because it's not used anymore
   //AutoScaleConfig? Autoscale
   ) : BaseEvent();


[EventType(EventType.ProxyCreated)]
public record EventProxyCreated(
   Region Region,
   Guid? ProxyId
   ) : BaseEvent();


[EventType(EventType.ProxyDeleted)]
public record EventProxyDeleted(
   Region Region,
   Guid? ProxyId
) : BaseEvent();


[EventType(EventType.ProxyFailed)]
public record EventProxyFailed(
   Region Region,
   Guid? ProxyId,
   Error Error
) : BaseEvent();


[EventType(EventType.ProxyStateUpdated)]
public record EventProxyStateUpdated(
   Region Region,
   Guid ProxyId,
   VmState State
   ) : BaseEvent();


[EventType(EventType.NodeCreated)]
public record EventNodeCreated(
    Guid MachineId,
    Guid? ScalesetId,
    PoolName PoolName
    ) : BaseEvent();

[EventType(EventType.NodeHeartbeat)]
public record EventNodeHeartbeat(
    Guid MachineId,
    Guid? ScalesetId,
    PoolName PoolName
    ) : BaseEvent();


[EventType(EventType.NodeDeleted)]
public record EventNodeDeleted(
    Guid MachineId,
    Guid? ScalesetId,
    PoolName PoolName,
    NodeState? MachineState
) : BaseEvent();


[EventType(EventType.ScalesetStateUpdated)]
public record EventScalesetStateUpdated(
    Guid ScalesetId,
    PoolName PoolName,
    ScalesetState State
) : BaseEvent();

[EventType(EventType.NodeStateUpdated)]
public record EventNodeStateUpdated(
    Guid MachineId,
    Guid? ScalesetId,
    PoolName PoolName,
    NodeState state
    ) : BaseEvent();

[EventType(EventType.CrashReported)]
public record EventCrashReported(
    Report Report,
    Container Container,
    [property: JsonPropertyName("filename")] String FileName,
    TaskConfig? TaskConfig
) : BaseEvent();


[EventType(EventType.RegressionReported)]
public record EventRegressionReported(
    RegressionReport RegressionReport,
    Container Container,
    [property: JsonPropertyName("filename")] String FileName,
    TaskConfig? TaskConfig
) : BaseEvent();


[EventType(EventType.FileAdded)]
public record EventFileAdded(
    Container Container,
    [property: JsonPropertyName("filename")] String FileName
) : BaseEvent();


[EventType(EventType.InstanceConfigUpdated)]
public record EventInstanceConfigUpdated(
    InstanceConfig Config
) : BaseEvent();

public record EventMessage(
    Guid EventId,
    EventType EventType,
    [property: TypeDiscrimnatorAttribute("EventType", typeof(EventTypeProvider))]
    [property: JsonConverter(typeof(BaseEventConverter))]
    BaseEvent Event,
    Guid InstanceId,
    String InstanceName
);

public class BaseEventConverter : JsonConverter<BaseEvent> {
    public override BaseEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        return null;
    }

    public override void Write(Utf8JsonWriter writer, BaseEvent value, JsonSerializerOptions options) {
        var eventType = value.GetType();
        JsonSerializer.Serialize(writer, value, eventType, options);
    }
}
