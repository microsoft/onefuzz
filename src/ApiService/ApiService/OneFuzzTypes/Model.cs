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
    free,
    SettingUp,
    Rebooting,
    Ready,
    Busy,
    Done,
    Shutdown,
    Halt,
}


public partial record Node
(
    DateTimeOffset? InitializedAt,
    [PartitionKey] PoolName PoolName,
    Guid? PoolId,
    [RowKey] Guid MachineId,
    NodeState State,
    Guid? ScalesetId,
    DateTimeOffset Heartbeat,
    Version Version,
    bool ReimageRequested,
    bool DeleteRequested,
    bool DebugKeepNode
) : EntityBase();


public record Error (ErrorCode Code, string[]? Errors = null);

public record UserInfo (Guid? ApplicationId, Guid? ObjectId, String? Upn);


public record EventMessage(
    Guid EventId,
    EventType EventType,
    BaseEvent Event,
    Guid InstanceId,
    String InstanceName
): EntityBase();


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

