using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.OneFuzz.Service;

public record BaseRequest{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
};

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
) : BaseRequest;

public record JobCreate(
    string Project,
    string Name,
    string Build,
    long Duration,
    string? Logs
) : BaseRequest;

public record JobSearch(
    Guid? JobId = null,
    List<JobState>? State = null,
    List<TaskState>? TaskState = null,
    bool? WithTasks = null
) : BaseRequest;

public record NodeAddSshKeyPost(Guid MachineId, string PublicKey) : BaseRequest;

public record ReproGet(Guid? VmId) : BaseRequest;

public record ReproCreate(
    Container Container,
    string Path,
    long Duration
) : BaseRequest;

public record ProxyGet(
    Guid? ScalesetId,
    Guid? MachineId,
    int? DstPort
): BaseRequest;

public record ProxyCreate(
    Guid ScalesetId,
    Guid MachineId,
    int DstPort,
    int Duration
) : BaseRequest;

public record ProxyDelete(
    Guid ScalesetId,
    Guid MachineId,
    int? DstPort
) : BaseRequest;

public record ProxyReset(
    string Region
) : BaseRequest;

public record ScalesetCreate(
    PoolName PoolName,
    string VmSku,
    string Image,
    string? Region,
    [property: Range(1, long.MaxValue)]
    long Size,
    bool SpotInstances,
    Dictionary<string, string> Tags,
    bool EphemeralOsDisks = false,
    AutoScaleOptions? AutoScale = null
) : BaseRequest;

public record AutoScaleOptions(
    [property: Range(0, long.MaxValue)] long Min,
    [property: Range(1, long.MaxValue)] long Max,
    [property: Range(0, long.MaxValue)] long Default,
    [property: Range(1, long.MaxValue)] long ScaleOutAmount,
    [property: Range(1, long.MaxValue)] long ScaleOutCooldown,
    [property: Range(1, long.MaxValue)] long ScaleInAmount,
    [property: Range(1, long.MaxValue)] long ScaleInCooldown
);

public record ScalesetSearch(
    Guid? ScalesetId = null,
    List<ScalesetState>? State = null,
    bool IncludeAuth = false
) : BaseRequest;

public record ScalesetStop(
    Guid ScalesetId,
    bool Now
) : BaseRequest;

public record ScalesetUpdate(
    Guid ScalesetId,
    [property: Range(1, long.MaxValue)]
    long? Size
) : BaseRequest;

public record TaskGet(Guid TaskId) : BaseRequest;

public record TaskCreate(
   Guid JobId,
   List<Guid>? PrereqTasks,
   TaskDetails Task,
   [Required]
   TaskPool Pool,
   List<TaskContainers>? Containers = null,
   Dictionary<string, string>? Tags = null,
   List<TaskDebugFlag>? Debug = null,
   bool? Colocate = null
) : BaseRequest;

public record TaskSearch(
    Guid? JobId,
    Guid? TaskId,
    List<TaskState> State) : BaseRequest;

public record PoolSearch(
    Guid? PoolId = null,
    PoolName? Name = null,
    List<PoolState>? State = null
) : BaseRequest;

public record PoolStop(
    PoolName Name,
    bool Now
) : BaseRequest;

public record PoolCreate(
    PoolName Name,
    Os Os,
    Architecture Arch,
    bool Managed,
    Guid? ClientId = null
) : BaseRequest;

public record WebhookCreate(
    string Name,
    Uri Url,
    List<EventType> EventTypes,
    string? SecretToken,
    WebhookMessageFormat? MessageFormat
) : BaseRequest;

public record WebhookSearch(Guid? WebhookId) : BaseRequest;

public record WebhookGet(Guid WebhookId) : BaseRequest;

public record WebhookUpdate(
    Guid WebhookId,
    string? Name,
    Uri? Url,
    List<EventType>? EventTypes,
    string? SecretToken,
    WebhookMessageFormat? MessageFormat
) : BaseRequest;
