using System.Text.Json.Serialization;

namespace Microsoft.OneFuzz.Service;

public record BaseRequest();

public record CanScheduleRequest(
    Guid MachineId,
    Guid TaskId
) : BaseRequest;

public record NodeCommandGet(
    Guid MachineId
) : BaseRequest;

public record NodeCommandDelete(
    Guid MachineId,
    string MessageId
) : BaseRequest;

public record NodeGet(
    Guid MachineId
) : BaseRequest;

public record NodeUpdate(
    Guid MachineId,
    bool? DebugKeepNode
) : BaseRequest;

public record NodeSearch(
    Guid? MachineId = null,
    List<NodeState>? State = null,
    Guid? ScalesetId = null,
    PoolName? PoolName = null
) : BaseRequest;

public record NodeStateEnvelope(
    NodeEventBase Event,
    Guid MachineId
) : BaseRequest;

// either NodeEvent or WorkerEvent
[JsonConverter(typeof(SubclassConverter<NodeEventBase>))]
public abstract record NodeEventBase;

public record NodeEvent(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    NodeStateUpdate? StateUpdate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkerEvent? WorkerEvent
) : NodeEventBase;

public record WorkerEvent(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkerDoneEvent? Done = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkerRunningEvent? Running = null
) : NodeEventBase;

public record WorkerRunningEvent(
    Guid TaskId);

public record WorkerDoneEvent(
    Guid TaskId,
    ExitStatus ExitStatus,
    string Stderr,
    string Stdout);

public record NodeStateUpdate(
    NodeState State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    NodeStateData? Data = null
) : NodeEventBase;

// NodeSettingUpEventData, NodeDoneEventData, or ProcessOutput
[JsonConverter(typeof(SubclassConverter<NodeStateData>))]
public abstract record NodeStateData;

public record NodeSettingUpEventData(
    List<Guid> Tasks
) : NodeStateData;

public record NodeDoneEventData(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Error,

    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ProcessOutput? ScriptOutput
) : NodeStateData;

public record ProcessOutput(
    ExitStatus ExitStatus,
    string Stderr,
    string Stdout
) : NodeStateData;

public record ExitStatus(
    int? Code,
    int? Signal,
    bool Success);

public record ContainerGet(
    Container Name
) : BaseRequest;

public record ContainerCreate(
    Container Name,
    IDictionary<string, string>? Metadata = null
) : BaseRequest;

public record ContainerDelete(
    Container Name,
    IDictionary<string, string>? Metadata = null
) : BaseRequest;

public record NotificationCreate(
    Container Container,
    bool ReplaceExisting,
    NotificationTemplate Config
) : BaseRequest;

public record NotificationSearch(
    List<Container>? Container
) : BaseRequest;

public record NotificationGet(
    Guid NotificationId
) : BaseRequest;

public record JobGet(
    Guid JobId
);

public record JobSearch(
    Guid? JobId = null,
    List<JobState>? State = null,
    List<TaskState>? TaskState = null,
    bool? WithTasks = null
);

public record NodeAddSshKeyPost(Guid MachineId, string PublicKey);

public record ReproGet(Guid? VmId);

public record ProxyGet(
    Guid? ScalesetId,
    Guid? MachineId,
    int? DstPort);

public record ProxyCreate(
    Guid ScalesetId,
    Guid MachineId,
    int DstPort,
    int Duration
);

public record ProxyDelete(
    Guid ScalesetId,
    Guid MachineId,
    int? DstPort
);

public record ProxyReset(
    string Region
);

public record TaskGet(Guid TaskId);

public record TaskSearch(
    Guid? JobId,
    Guid? TaskId,
    List<TaskState> State);

public record PoolSearch(
    Guid? PoolId = null,
    PoolName? Name = null,
    List<PoolState>? State = null
);

public record PoolStop(
    PoolName Name,
    bool Now
);

public record PoolCreate(
    PoolName Name,
    Os Os,
    Architecture Arch,
    bool Managed,
    Guid? ClientId = null
);
