using Azure.Data.Tables;
using System;
using System.Runtime.Serialization;

namespace ApiService;


record NodeCommandStopIfFree { }

record StopNodeCommand{}

record StopTaskNodeCommand{
    Guid TaskId;
}

record NodeCommandAddSshKey{
    string PublicKey;
}

record NodeCommand
{
    StopNodeCommand? Stop;
    StopTaskNodeCommand? StopTask;
    NodeCommandAddSshKey? AddSshKey;
    NodeCommandStopIfFree? StopIfFree;
}

enum NodeTaskState
{
    init,
    setting_up,
    running,
}

record NodeTasks
{
    Guid MachineId;
    Guid TaskId;
    NodeTaskState State = NodeTaskState.init;

}

enum NodeState
{
    init,
    free,
    setting_up,
    rebooting,
    ready,
    busy,
    done,
    shutdown,
    halt,
}


record Node : ITableEntity
{
	[DataMember(Name = "initialized_at")]
	public DateTimeOffset? InitializedAt;
	[DataMember(Name = "pool_name")]
	public string PoolName;
	[DataMember(Name = "pool_id")]
	public Guid? PoolId;
	[DataMember(Name = "machine_id")]
	public Guid MachineId;
	[DataMember(Name = "state")]
	public NodeState State;
	[DataMember(Name = "scaleset_id")]
	public Guid? ScalesetId;
	[DataMember(Name = "heartbeat")]
	public DateTimeOffset Heartbeat;
	[DataMember(Name = "version")]
	public Version Version;
	[DataMember(Name = "reimage_requested")]
	public bool ReimageRequested;
	[DataMember(Name = "delete_requested")]
	public bool DeleteRequested;
	[DataMember(Name = "debug_keep_node")]
	public bool DebugKeepNode;

	public string PartitionKey { get => PoolName; set => PoolName = value; }
	public string RowKey { get => MachineId.ToString(); set => MachineId = Guid.Parse(value); }
	public Azure.ETag ETag { get; set; }
	DateTimeOffset? ITableEntity.Timestamp { get; set; }

}