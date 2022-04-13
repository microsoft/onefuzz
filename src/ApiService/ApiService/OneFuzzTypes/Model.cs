using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using System;
using System.Collections.Generic;
using PoolName = System.String;

namespace Microsoft.OneFuzz.Service;


/// Convention for database entities:
/// All entities are represented by immutable records
/// All database entities need to derive from EntityBase
/// Only properties that also apears as parameter initializers are mapped to the database
/// The name of the property will be tranlated to snake case and used as the column name
/// It is possible to rename the column name by using the [property:JsonPropertyName("column_name")] attribute
/// the "partion key" and "row key" are identified by the [PartitionKey] and [RowKey] attributes
/// Guids are mapped to string in the db

public record Authentication
(
    string Password,
    string PublicKey,
    string PrivateKey
);

[SkipRename]
public enum HeartbeatType
{
    MachineAlive,
    TaskAlive,
}

public record HeartbeatData(HeartbeatType type);

public record NodeHeartbeatEntry(Guid NodeId, HeartbeatData[] data);

public record NodeCommandStopIfFree();

public record StopNodeCommand();

public record StopTaskNodeCommand(Guid TaskId);

public record NodeCommandAddSshKey(string PublicKey);


public record NodeCommand
(
    StopNodeCommand? Stop,
    StopTaskNodeCommand? StopTask,
    NodeCommandAddSshKey? AddSshKey,
    NodeCommandStopIfFree? StopIfFree
);

public enum NodeTaskState
{
    Init,
    SettingUp,
    Running,
}

public record NodeTasks
(
    Guid MachineId,
    Guid TaskId,
    NodeTaskState State = NodeTaskState.Init
);

public enum NodeState
{
    Init,
    Free,
    SettingUp,
    Rebooting,
    Ready,
    Busy,
    Done,
    Shutdown,
    Halt,
}

public record ProxyHeartbeat
(
    string Region,
    Guid ProxyId,
    List<ProxyForward> Forwards,
    DateTimeOffset TimeStamp
);

public partial record Node
(
    DateTimeOffset? InitializedAt,
    [PartitionKey] PoolName PoolName,
    Guid? PoolId,
    [RowKey] Guid MachineId,
    NodeState State,
    Guid? ScalesetId,
    DateTimeOffset Heartbeat,
    string Version,
    bool ReimageRequested,
    bool DeleteRequested,
    bool DebugKeepNode
) : EntityBase();


public partial record ProxyForward
(
    [PartitionKey] string Region,
    [RowKey] int DstPort,
    int SrcPort,
    string DstIp
) : EntityBase();

public partial record ProxyConfig
(
    Uri Url,
    string Notification,
    string Region,
    Guid? ProxyId,
    List<ProxyForward> Forwards,
    string InstanceTelemetryKey,
    string MicrosoftTelemetryKey

);

public partial record Proxy
(
    [PartitionKey] string Region,
    [RowKey] Guid ProxyId,
    DateTimeOffset? CreatedTimestamp,
    VmState State,
    Authentication Auth,
    string? Ip,
    Error? Error,
    string Version,
    ProxyHeartbeat? heartbeat
) : EntityBase();

public record Error(ErrorCode Code, string[]? Errors = null);

public record UserInfo(Guid? ApplicationId, Guid? ObjectId, String? Upn);


public record EventMessage(
    Guid EventId,
    EventType EventType,
    BaseEvent Event,
    Guid InstanceId,
    String InstanceName
) : EntityBase();

public record Update(
    UpdateType UpdateType,
    [PartitionKey] string? ParititionKey,
    [RowKey] string? RowKey,
    string? Method
);

// TODO: We probably need to do some json deserialization magic to make this work with strings
public enum UpdateType
{
    Task,
    Job,
    Repro,
    Proxy,
    Pool,
    Node,
    Scaleset,
    TaskScheduler
}

//record AnyHttpUrl(AnyUrl):
//    allowed_schemes = {'http', 'https
//





//public record TaskConfig(
//    Guid jobId,
//    List<Guid> PrereqTasks,
//    TaskDetails Task,
//    TaskVm? vm,
//    TaskPool pool: Optional[]
//    containers: List[TaskContainers]
//    tags: Dict[str, str]
//    debug: Optional[List[TaskDebugFlag]]
//    colocate: Optional[bool]
//    ): EntityBase();

