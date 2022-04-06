using Azure.Data.Tables;
using System;
using System.Runtime.Serialization;
using System.Collections.Generic;

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

record ProxyForward : ITableEntity 
{ 
	[DataMember(Name = "region")]
	public string Region;
	[DataMember(Name = "src_port")]
	public int SrcPort;
    [DataMember(Name = "dst_port")]
	public int DstPort;
    [DataMember(Name = "dst_ip")]
	public int DstIp;

	public string PartitionKey { get => Region; set => Region = value; }
	public string RowKey { get => SrcPort.ToString(); set => SrcPort = Int32.Parse(value); }
	public Azure.ETag ETag { get; set; }
	DateTimeOffset? ITableEntity.Timestamp { get; set; }

}

record ProxyConfig : ITableEntity
{	
	[DataMember(Name = "url")]
	public string Url;
	[DataMember(Name = "notification")]
	public string Notifcation;
	[DataMember(Name = "region")]
	public string Region;
	[DataMember(Name = "proxy_id")]
	public Guid? ProxyId;
	[DataMember(Name = "forwards")]
	public List<ProxyForward> Forwards;
	[DataMember(Name = "instance_telemetry_key")]
	public string InstanceTelemetryKey;
	[DataMember(Name = "microsoft_telemetry_key")]
	public string MicrosoftTelemetryKey;

	public string PartitionKey { get => Region; set => Region = value; }
	public string RowKey { get => ProxyId.ToString(); set => ProxyId = Guid.Parse(value); }
	public Azure.ETag ETag { get; set; }
	DateTimeOffset? ITableEntity.Timestamp { get; set; }

}