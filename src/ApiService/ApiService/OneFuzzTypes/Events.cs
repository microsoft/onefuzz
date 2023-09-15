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
    NotificationFailed,
    WebhookSucceeded,
    WebhookRetried,
    WebhookFailed
}

public abstract record BaseEvent() {
    private static readonly IReadOnlyDictionary<Type, EventType> _typeToEvent;
    private static readonly IReadOnlyDictionary<EventType, Type> _eventToType;

    static BaseEvent() {
        static EventType ExtractEventType(Type type) {
            var attr = type.GetCustomAttribute<EventTypeAttribute>();
            if (attr is null) {
                throw new InvalidOperationException($"Type {type} is missing {nameof(EventTypeAttribute)}");
            }
            return attr.EventType;
        }

        _typeToEvent =
            typeof(BaseEvent).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(BaseEvent)))
            .ToDictionary(x => x, ExtractEventType);

        _eventToType = _typeToEvent.ToDictionary(x => x.Value, x => x.Key);

        // check that all event types are accounted for
        var allEventTypes = Enum.GetValues<EventType>();
        var missingEventTypes = allEventTypes.Except(_eventToType.Keys).ToList();
        if (missingEventTypes.Any()) {
            throw new InvalidOperationException($"Missing event types: {string.Join(", ", missingEventTypes)}");
        }
    }


    public EventType GetEventType() {
        var type = this.GetType();
        if (_typeToEvent.TryGetValue(type, out var eventType)) {
            return eventType;
        }

        throw new NotSupportedException($"Unknown event type: {type.GetType()}");
    }

    public static Type GetTypeInfo(EventType eventType) {
        if (_eventToType.TryGetValue(eventType, out var type)) {
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
    StoredUserInfo? UserInfo,
    TaskConfig Config
) : BaseEvent();

[EventType(EventType.TaskFailed)]
public record EventTaskFailed(
    Guid JobId,
    Guid TaskId,
    Error Error,
    StoredUserInfo? UserInfo,
    TaskConfig Config
    ) : BaseEvent();


[EventType(EventType.JobCreated)]
public record EventJobCreated(
   Guid JobId,
   JobConfig Config,
   StoredUserInfo? UserInfo
   ) : BaseEvent();


public record JobTaskStopped(
    Guid TaskId,
    TaskType TaskType,
    Error? Error
    );


[EventType(EventType.JobStopped)]
public record EventJobStopped(
    Guid JobId,
    JobConfig Config,
    StoredUserInfo? UserInfo,
    List<JobTaskStopped> TaskInfo
) : BaseEvent(), ITruncatable<BaseEvent> {
    public BaseEvent Truncate(int maxLength) {
        return this with {
            Config = Config.Truncate(maxLength)
        };
    }
}


[EventType(EventType.TaskCreated)]
public record EventTaskCreated(
    Guid JobId,
    Guid TaskId,
    TaskConfig Config,
    StoredUserInfo? UserInfo
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
   string? Project,
   string? Name,
   TaskState? State,
   TaskConfig? Config
) : BaseEvent();

[EventType(EventType.Ping)]
public record EventPing(
    Guid PingId
) : BaseEvent();


[EventType(EventType.ScalesetCreated)]
public record EventScalesetCreated(
   ScalesetId ScalesetId,
   PoolName PoolName,
   string VmSku,
   string Image,
   Region Region,
   int Size) : BaseEvent();


[EventType(EventType.ScalesetFailed)]
public sealed record EventScalesetFailed(
    ScalesetId ScalesetId,
    PoolName PoolName,
    Error Error
) : BaseEvent();


[EventType(EventType.ScalesetDeleted)]
public record EventScalesetDeleted(
   ScalesetId ScalesetId,
   PoolName PoolName

   ) : BaseEvent();


[EventType(EventType.ScalesetResizeScheduled)]
public record EventScalesetResizeScheduled(
    ScalesetId ScalesetId,
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
    ScalesetId? ScalesetId,
    PoolName PoolName
    ) : BaseEvent();

[EventType(EventType.NodeHeartbeat)]
public record EventNodeHeartbeat(
    Guid MachineId,
    ScalesetId? ScalesetId,
    PoolName PoolName,
    NodeState state
    ) : BaseEvent();


[EventType(EventType.NodeDeleted)]
public record EventNodeDeleted(
    Guid MachineId,
    ScalesetId? ScalesetId,
    PoolName PoolName,
    NodeState? MachineState
) : BaseEvent();


[EventType(EventType.ScalesetStateUpdated)]
public record EventScalesetStateUpdated(
    ScalesetId ScalesetId,
    PoolName PoolName,
    ScalesetState State
) : BaseEvent();

[EventType(EventType.NodeStateUpdated)]
public record EventNodeStateUpdated(
    Guid MachineId,
    ScalesetId? ScalesetId,
    PoolName PoolName,
    NodeState state
    ) : BaseEvent();

[EventType(EventType.CrashReported)]
public record EventCrashReported(
    Report Report,
    Container Container,
    [property: JsonPropertyName("filename")] String FileName,
    TaskConfig? TaskConfig
) : BaseEvent(), ITruncatable<BaseEvent> {
    public BaseEvent Truncate(int maxLength) {
        return this with {
            Report = Report.Truncate(maxLength)
        };
    }
}

[EventType(EventType.RegressionReported)]
public record EventRegressionReported(
    RegressionReport RegressionReport,
    Container Container,
    [property: JsonPropertyName("filename")] String FileName,
    TaskConfig? TaskConfig
) : BaseEvent(), ITruncatable<BaseEvent> {
    public BaseEvent Truncate(int maxLength) {
        return this with {
            RegressionReport = RegressionReport.Truncate(maxLength)
        };
    }
}

[EventType(EventType.FileAdded)]
public record EventFileAdded(
    Container Container,
    [property: JsonPropertyName("filename")] String FileName
) : BaseEvent();


[EventType(EventType.InstanceConfigUpdated)]
public record EventInstanceConfigUpdated(
    InstanceConfig Config
) : BaseEvent();

[EventType(EventType.NotificationFailed)]
public record EventNotificationFailed(
    Guid NotificationId,
    Guid JobId,
    Error? Error
) : BaseEvent();

[EventType(EventType.WebhookSucceeded)]
public record EventWebhookSucceeded(
    Guid WebhookId,
    Guid EventId,
    EventType eventType,
    string? data
) : BaseEvent();

[EventType(EventType.WebhookRetried)]
public record EventWebhookRetried(
    Guid WebhookId,
    Guid EventId,
    EventType eventType,
    string? data
) : BaseEvent();

[EventType(EventType.WebhookFailed)]
public record EventWebhookFailed(
    Guid WebhookId,
    Guid EventId,
    EventType eventType,
    string? data
) : BaseEvent();

public record DownloadableEventMessage : EventMessage, ITruncatable<DownloadableEventMessage> {
    public Uri SasUrl { get; init; }
    public DateOnly? ExpiresOn { get; init; }

    public DownloadableEventMessage(Guid EventId, EventType EventType, BaseEvent Event, Guid InstanceId, string InstanceName, DateTime CreatedAt, Uri SasUrl, DateOnly? ExpiresOn)
        : base(EventId, EventType, Event, InstanceId, InstanceName, CreatedAt) {
        this.SasUrl = SasUrl;
        this.ExpiresOn = ExpiresOn;
    }

    public override DownloadableEventMessage Truncate(int maxLength) {
        if (this.Event is ITruncatable<BaseEvent> truncatableEvent) {
            return this with {
                Event = truncatableEvent.Truncate(maxLength)
            };
        } else {
            return this;
        }
    }
}

public record EventMessage(
    Guid EventId,
    EventType EventType,
    [property: TypeDiscrimnator("EventType", typeof(EventTypeProvider))]
    [property: JsonConverter(typeof(BaseEventConverter))]
    BaseEvent Event,
    Guid InstanceId,
    String InstanceName,
    DateTime CreatedAt,
    String Version = "1.0"
) : ITruncatable<EventMessage>, IRetentionPolicy {
    public virtual EventMessage Truncate(int maxLength) {
        if (this.Event is ITruncatable<BaseEvent> truncatableEvent) {
            return this with {
                Event = truncatableEvent.Truncate(maxLength)
            };
        } else {
            return this;
        }
    }

    public DateOnly GetExpiryDate() => Event switch {
        BaseEvent @event when @event is IRetentionPolicy retentionPolicy => retentionPolicy.GetExpiryDate(),
        _ => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(180))
    };
}

public class BaseEventConverter : JsonConverter<BaseEvent> {
    public override BaseEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        throw new NotSupportedException("BaseEvent cannot be read");
    }

    public override void Write(Utf8JsonWriter writer, BaseEvent value, JsonSerializerOptions options) {
        var eventType = value.GetType();
        JsonSerializer.Serialize(writer, value, eventType, options);
    }
}
