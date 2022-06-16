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
    string? Error,
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
