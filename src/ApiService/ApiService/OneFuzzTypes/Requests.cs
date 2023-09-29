using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.OneFuzz.Service;

public record BaseRequest {
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
};

public record CanScheduleRequest(
    [property: Required] Guid MachineId,
    [property: Required] Guid TaskId
) : BaseRequest;

public record NodeCommandGet(
    [property: Required] Guid MachineId
) : BaseRequest;

public record NodeCommandDelete(
    [property: Required] Guid MachineId,
    [property: Required] string MessageId
) : BaseRequest;

public record NodeGet(
    [property: Required] Guid MachineId
) : BaseRequest;

public record NodeUpdate(
    [property: Required] Guid MachineId,
    bool? DebugKeepNode
) : BaseRequest;

public record NodeSearch(
    Guid? MachineId = null,
    List<NodeState>? State = null,
    ScalesetId? ScalesetId = null,
    PoolName? PoolName = null
) : BaseRequest;

public record NodeStateEnvelope(
    [property: Required] NodeEventBase Event,
    [property: Required] Guid MachineId
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
    [property: Required] Guid TaskId);

public record WorkerDoneEvent(
    [property: Required] Guid TaskId,
    [property: Required] ExitStatus ExitStatus,
    [property: Required] string Stderr,
    [property: Required] string Stdout);

public record NodeStateUpdate(
    [property: Required] NodeState State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    NodeStateData? Data = null
) : NodeEventBase;

// NodeSettingUpEventData, NodeDoneEventData, or ProcessOutput
[JsonConverter(typeof(SubclassConverter<NodeStateData>))]
public abstract record NodeStateData;

public record NodeSettingUpEventData(
   [property: Required] List<Guid> Tasks
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
    [property: Required] Container Name
) : BaseRequest;

public record ContainerCreate(
    [property: Required] Container Name,
    IDictionary<string, string>? Metadata = null
) : BaseRequest;

public record ContainerDelete(
    [property: Required] Container Name,
    IDictionary<string, string>? Metadata = null
) : BaseRequest;

public record NotificationCreate(
    [property: Required] Container Container,
    [property: Required] bool ReplaceExisting,
    [property: Required] NotificationTemplate Config
) : BaseRequest;

public record NotificationSearch(
    List<Container>? Container,
    Guid? NotificationId
) : BaseRequest;


public record NotificationTest(
    [property: Required] IReport Report,
    [property: Required] Notification Notification
) : BaseRequest;

public record NotificationGet(
    [property: Required] Guid NotificationId
) : BaseRequest;

public record JobGet(
    [property: Required] Guid JobId
) : BaseRequest;

public record JobCreate(
    [property: Required] string Project,
    [property: Required] string Name,
    [property: Required] string Build,
    [property: Required] long Duration,
    string? Logs
) : BaseRequest;

public record JobSearch(
    Guid? JobId = null,
    List<JobState>? State = null,
    List<TaskState>? TaskState = null,
    bool? WithTasks = null
) : BaseRequest;

public record NodeAddSshKeyPost(
    [property: Required] Guid MachineId,
    [property: Required] string PublicKey
) : BaseRequest;

public record ReproGet(Guid? VmId) : BaseRequest;

public record ReproCreate(
    [property: Required] Container Container,
    [property: Required] string Path,
    [property: Required] long Duration
) : BaseRequest;

public record ProxyGet(
    ScalesetId? ScalesetId,
    Guid? MachineId,
    int? DstPort
) : BaseRequest;

public record ProxyCreate(
    [property: Required] ScalesetId ScalesetId,
    [property: Required] Guid MachineId,
    [property: Required] int DstPort,
    [property: Required] int Duration
) : BaseRequest;

public record ProxyDelete(
    [property: Required] ScalesetId ScalesetId,
    [property: Required] Guid MachineId,
    int? DstPort
) : BaseRequest;

public record ProxyReset(
    [property: Required] string Region
) : BaseRequest;

public record ScalesetCreate(
    [property: Required] PoolName PoolName,
    [property: Required] string VmSku,
    ImageReference? Image,
    Region? Region,
    [property: Range(1, long.MaxValue), Required] long Size,
    [property: Required] bool SpotInstances,
    [property: Required] Dictionary<string, string> Tags,
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
    ScalesetId? ScalesetId = null,
    List<ScalesetState>? State = null,
    bool IncludeAuth = false
) : BaseRequest;

public record ScalesetStop(
    [property: Required] ScalesetId ScalesetId,
    [property: Required] bool Now
) : BaseRequest;

public record ScalesetUpdate(
    [property: Required] ScalesetId ScalesetId,
    [property: Range(1, long.MaxValue)]
    long? Size
) : BaseRequest;

public record TaskGet(
    [property: Required] Guid TaskId
) : BaseRequest;

public record TaskCreate(
   [property: Required] Guid JobId,
   List<Guid>? PrereqTasks,
   [property: Required] TaskDetails Task,
   [property: Required] TaskPool Pool,
   List<TaskContainers>? Containers = null,
   Dictionary<string, string>? Tags = null,
   List<TaskDebugFlag>? Debug = null,
   bool? Colocate = null
) : BaseRequest;

public record TaskSearch(
    Guid? JobId,
    Guid? TaskId,
    List<TaskState>? State = null
) : BaseRequest;

public record PoolSearch(
    Guid? PoolId = null,
    PoolName? Name = null,
    List<PoolState>? State = null
) : BaseRequest;

public record PoolStop(
    [property: Required] PoolName Name,
    [property: Required] bool Now
) : BaseRequest;

public record PoolCreate(
    [property: Required] PoolName Name,
    [property: Required] Os Os,
    [property: Required] Architecture Arch,
    [property: Required] bool Managed,
    Guid? ObjectId = null,
    bool Update = false
) : BaseRequest;


public record PoolUpdate(
    [property: Required] PoolName Name,
    Guid? ObjectId = null
) : BaseRequest;

public record WebhookCreate(
    [property: Required] string Name,
    [property: Required] Uri Url,
    [property: Required] List<EventType> EventTypes,
    string? SecretToken,
    WebhookMessageFormat? MessageFormat
) : BaseRequest;

public record WebhookSearch(Guid? WebhookId) : BaseRequest;

public record WebhookGet([property: Required] Guid WebhookId) : BaseRequest;

public record WebhookUpdate(
    [property: Required] Guid WebhookId,
    string? Name,
    Uri? Url,
    List<EventType>? EventTypes,
    string? SecretToken,
    WebhookMessageFormat? MessageFormat
) : BaseRequest;

public record InstanceConfigUpdate(
    [property: Required] InstanceConfig config
) : BaseRequest;


public record AgentRegistrationGet(
    [property: Required] Guid MachineId
) : BaseRequest;


public record AgentRegistrationPost(
    [property: Required] PoolName PoolName,
    ScalesetId? ScalesetId,
    [property: Required] Guid MachineId,
    Os? Os,
    string? MachineName,
    [property: Required] string Version = "1.0.0"
) : BaseRequest;

public record TemplateValidationPost(
    [property: Required] string Template,
    TemplateRenderContext? Context
) : BaseRequest;

public record JinjaToScribanMigrationPost(
    bool DryRun = false
) : BaseRequest;

public record EventsGet(
    [property: Required] Guid EventId
) : BaseRequest;
