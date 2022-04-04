using Azure.Data.Tables;
using Microsoft.OneFuzz.Service;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ApiService;


/// Convention for databse entoties:
/// All entities are represented by immuable records
/// Only properties that also apears as parameter initializers are mapped to the database
/// The name of the property will be tranlated to snake case and used as the column name
/// It is possible to rename the column name by using the [property:JsonPropertyName("column_name")] attribute
/// the "partion key" and "row key" are identified by the [PartitionKey] and [RowKey] attributes
/// Guids are mapped to string in the db


record NodeHeartbeatEntry(Guid NodeId, Dictionary<string, HeartbeatType>[] data);

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
    [PartitionKey] string PoolName,
    Guid? PoolId,
    [RowKey] Guid MachineId,
    NodeState State,
    Guid? ScalesetId,
    DateTimeOffset Heartbeat,
    Version Version,
    bool ReimageRequested,
    bool DeleteRequested,
    bool DebugKeepNode
): EntityBase();
