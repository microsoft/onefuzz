using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Endpoint = System.String;
using GroupId = System.Guid;
using PoolName = System.String;
using PrincipalId = System.Guid;
using Region = System.String;

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
public enum HeartbeatType {
    MachineAlive,
    TaskAlive,
}

public record HeartbeatData(HeartbeatType Type);

public record TaskHeartbeatEntry(
    Guid TaskId,
    Guid? JobId,
    Guid MachineId,
    HeartbeatData[] Data
    );
public record NodeHeartbeatEntry(Guid NodeId, HeartbeatData[] Data);

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

public enum NodeTaskState {
    Init,
    SettingUp,
    Running,
}

public record NodeTasks
(
    Guid MachineId,
    Guid TaskId,
    NodeTaskState State = NodeTaskState.Init
) : StatefulEntityBase<NodeTaskState>(State);


public record ProxyHeartbeat
(
    Region Region,
    Guid ProxyId,
    List<ProxyForward> Forwards,
    DateTimeOffset TimeStamp
);

public record Node
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
) : StatefulEntityBase<NodeState>(State);


public record Forward
(
    int SrcPort,
    int DstPort,
    string DstIp
);


public record ProxyForward
(
    [PartitionKey] Region Region,
    int Port,
    Guid ScalesetId,
    Guid MachineId,
    Guid? ProxyId,
    [RowKey] int DstPort,
    string DstIp,
    DateTimeOffset EndTime
) : EntityBase();

public record ProxyConfig
(
    Uri Url,
    Uri Notification,
    Region Region,
    Guid? ProxyId,
    List<Forward> Forwards,
    string InstanceTelemetryKey,
    string MicrosoftTelemetryKey,
    Guid InstanceId

);

public record Proxy
(
    [PartitionKey] Region Region,
    [RowKey] Guid ProxyId,
    DateTimeOffset? CreatedTimestamp,
    VmState State,
    Authentication Auth,
    string? Ip,
    Error? Error,
    string Version,
    ProxyHeartbeat? Heartbeat,
    bool Outdated
) : StatefulEntityBase<VmState>(State);

public record Error(ErrorCode Code, string[]? Errors = null);

public record UserInfo(Guid? ApplicationId, Guid? ObjectId, String? Upn);




public record TaskDetails(

    TaskType Type,
    int Duration,
    string? TargetExe,
    Dictionary<string, string>? TargetEnv,
    List<string>? TargetOptions,
    int? TargetWorkers,
    bool? TargetOptionsMerge,
    bool? CheckAsanLog,
    bool? CheckDebugger,
    int? CheckRetryCount,
    bool? CheckFuzzerHelp,
    bool? ExpectCrashOnFailure,
    bool? RenameOutput,
    string? SupervisorExe,
    Dictionary<string, string>? SupervisorEnv,
    List<string>? SupervisorOptions,
    string? SupervisorInputMarker,
    string? GeneratorExe,
    Dictionary<string, string>? GeneratorEnv,
    List<string>? GeneratorOptions,
    string? AnalyzerExe,
    Dictionary<string, string>? AnalyzerEnv,
    List<string> AnalyzerOptions,
    ContainerType? WaitForFiles,
    string? StatsFile,
    StatsFormat? StatsFormat,
    bool? RebootAfterSetup,
    int? TargetTimeout,
    int? EnsembleSyncDelay,
    bool? PreserveExistingOutputs,
    List<string>? ReportList,
    int? MinimizedStackDepth,
    string? CoverageFilter
);

public record TaskVm(
    Region Region,
    string Sku,
    string Image,
    bool? RebootAfterSetup,
    int Count = 1,
    bool SpotInstance = false
);

public record TaskPool(
    int Count,
    PoolName PoolName
);

public record TaskContainers(
    ContainerType Type,
    Container Name
);
public record TaskConfig(
   Guid JobId,
   List<Guid>? PrereqTasks,
   TaskDetails Task,
   TaskVm? Vm,
   TaskPool? Pool,
   List<TaskContainers>? Containers,
   Dictionary<string, string>? Tags,
   List<TaskDebugFlag>? Debug,
   bool? Colocate
   );


public record TaskEventSummary(
    DateTimeOffset? Timestamp,
    string EventData,
    string EventType
    );


public record NodeAssignment(
    Guid NodeId,
    Guid? ScalesetId,
    NodeTaskState State
    );


public record Task(
    [PartitionKey] Guid JobId,
    [RowKey] Guid TaskId,
    TaskState State,
    Os Os,
    TaskConfig Config,
    Error? Error,
    Authentication? Auth,
    DateTimeOffset? Heartbeat,
    DateTimeOffset? EndTime,
    UserInfo? UserInfo) : StatefulEntityBase<TaskState>(State) {
    List<TaskEventSummary> Events { get; set; } = new List<TaskEventSummary>();
    List<NodeAssignment> Nodes { get; set; } = new List<NodeAssignment>();
}
public record AzureSecurityExtensionConfig();
public record GenevaExtensionConfig();


public record KeyvaultExtensionConfig(
    string KeyVaultName,
    string CertName,
    string CertPath,
    string ExtensionStore
);

public record AzureMonitorExtensionConfig(
    string ConfigVersion,
    string Moniker,
    string Namespace,
    [property: JsonPropertyName("monitoringGSEnvironment")] string MonitoringGSEnvironment,
    [property: JsonPropertyName("monitoringGCSAccount")] string MonitoringGCSAccount,
    [property: JsonPropertyName("monitoringGCSAuthId")] string MonitoringGCSAuthId,
    [property: JsonPropertyName("monitoringGCSAuthIdType")] string MonitoringGCSAuthIdType
);

public record AzureVmExtensionConfig(
    KeyvaultExtensionConfig? Keyvault,
    AzureMonitorExtensionConfig? AzureMonitor,
    AzureSecurityExtensionConfig? AzureSecurity,
    GenevaExtensionConfig? Geneva
);

public record NetworkConfig(
    string AddressSpace,
    string Subnet
) {
    public static NetworkConfig Default { get; } = new NetworkConfig("10.0.0.0/8", "10.0.0.0/16");


    public NetworkConfig() : this("10.0.0.0/8", "10.0.0.0/16") { }
}

public record NetworkSecurityGroupConfig(
    string[] AllowedServiceTags,
    string[] AllowedIps
) {
    public NetworkSecurityGroupConfig() : this(Array.Empty<string>(), Array.Empty<string>()) { }
}

public record ApiAccessRule(
    string[] Methods,
    Guid[] AllowedGroups
);

//# initial set of admins can only be set during deployment.
//# if admins are set, only admins can update instance configs.
//# if set, only admins can manage pools or scalesets
public record InstanceConfig
(
    [PartitionKey, RowKey] string InstanceName,
    Guid[]? Admins,
    bool? AllowPoolManagement,
    string[] AllowedAadTenants,
    NetworkConfig NetworkConfig,
    NetworkSecurityGroupConfig ProxyNsgConfig,
    AzureVmExtensionConfig? Extensions,
    string ProxyVmSku,
    IDictionary<Endpoint, ApiAccessRule>? ApiAccessRules,
    IDictionary<PrincipalId, GroupId[]>? GroupMembership,

    IDictionary<string, string>? VmTags,
    IDictionary<string, string>? VmssTags
) : EntityBase() {
    public InstanceConfig(string instanceName) : this(
        instanceName,
        null,
        true,
        Array.Empty<string>(),
        new NetworkConfig(),
        new NetworkSecurityGroupConfig(),
        null,
        "Standard_B2s",
        null,
        null,
        null,
        null) { }

    public InstanceConfig() : this(String.Empty) { }

    public List<Guid>? CheckAdmins(List<Guid>? value) {
        if (value is not null && value.Count == 0) {
            throw new ArgumentException("admins must be null or contain at least one UUID");
        } else {
            return value;
        }
    }


    //# At the moment, this only checks allowed_aad_tenants, however adding
    //# support for 3rd party JWT validation is anticipated in a future release.
    public ResultVoid<List<string>> CheckInstanceConfig() {
        List<string> errors = new();
        if (AllowedAadTenants.Length == 0) {
            errors.Add("allowed_aad_tenants must not be empty");
        }
        if (errors.Count == 0) {
            return ResultVoid<List<string>>.Ok();
        } else {
            return ResultVoid<List<string>>.Error(errors);
        }
    }
}


public record ScalesetNodeState(
    Guid MachineId,
    string InstanceId,
    NodeState? State

);


public record Scaleset(
    [PartitionKey] PoolName PoolName,
    [RowKey] Guid ScalesetId,
    ScalesetState State,
    Authentication? Auth,
    string VmSku,
    string Image,
    Region Region,
    int Size,
    bool SpotInstance,
    bool EphemeralOsDisks,
    bool NeedsConfigUpdate,
    Error? Error,
    List<ScalesetNodeState> Nodes,
    Guid? ClientId,
    Guid? ClientObjectId,
    Dictionary<string, string> Tags

) : StatefulEntityBase<ScalesetState>(State);

[JsonConverter(typeof(ContainerConverter))]
public record Container(string ContainerName) {
    public string ContainerName { get; } = ContainerName.All(c => char.IsLetterOrDigit(c) || c == '-') ? ContainerName : throw new ArgumentException("Container name must have only numbers, letters or dashes");
}

public class ContainerConverter : JsonConverter<Container> {
    public override Container? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        var containerName = reader.GetString();
        return containerName == null ? null : new Container(containerName);
    }

    public override void Write(Utf8JsonWriter writer, Container value, JsonSerializerOptions options) {
        writer.WriteStringValue(value.ContainerName);
    }
}

public record Notification(
    Container Container,
    Guid NotificationId,
    NotificationTemplate Config
) : EntityBase();

public record BlobRef(
    string Account,
    Container container,
    string name
);

public record Report(
    string? InputUrl,
    BlobRef? InputBlob,
    string Executable,
    string CrashType,
    string CrashSite,
    List<string> CallStack,
    string CallStackSha256,
    string InputSha256,
    string? AsanLog,
    Guid TaskId,
    Guid JobId,
    int? ScarinessScore,
    string? ScarinessDescription,
    List<string>? MinimizedStack,
    string? MinimizedStackSha256,
    List<string>? MinimizedStackFunctionNames,
    string? MinimizedStackFunctionNamesSha256,
    List<string>? MinimizedStackFunctionLines,
    string? MinimizedStackFunctionLinesSha256
);

public record NoReproReport(
    string InputSha,
    BlobRef? InputBlob,
    string? Executable,
    Guid TaskId,
    Guid JobId,
    int Tries,
    string? Error
);

public record CrashTestResult(
    Report? CrashReport,
    NoReproReport? NoReproReport
);

public record RegressionReport(
    CrashTestResult CrashTestResult,
    CrashTestResult? OriginalCrashTestResult
);

public record NotificationTemplate(
    AdoTemplate? AdoTemplate,
    TeamsTemplate? TeamsTemplate,
    GithubIssuesTemplate? GithubIssuesTemplate
);

public record AdoTemplate();

public record TeamsTemplate();

public record GithubIssuesTemplate();

public record Repro(
    [PartitionKey] Guid VmId,
    [RowKey] Guid _,
    Guid TaskId,
    ReproConfig Config,
    VmState State,
    Authentication? Auth,
    Os Os,
    Error? Error,
    string? Ip,
    DateTime? EndTime,
    UserInfo? UserInfo
) : StatefulEntityBase<VmState>(State);

// TODO: Make this >1 and < 7*24 (more than one hour, less than seven days)
public record ReproConfig(
    Container Container,
    string Path,
    int Duration
);

// Skipping AutoScaleConfig because it's not used anymore
public record Pool(
    DateTimeOffset Timestamp,
    PoolName Name,
    Guid PoolId,
    Os Os,
    bool Managed,
    Architecture Architecture,
    PoolState State,
    Guid? ClientId,
    List<Node>? Nodes,
    AgentConfig? Config,
    List<WorkSetSummary>? WorkQueue,
    List<ScalesetSummary>? ScalesetSummary
) : StatefulEntityBase<PoolState>(State);


public record ClientCredentials
(
    Guid ClientId,
    string ClientSecret
);


public record AgentConfig(
    ClientCredentials? ClientCredentials,
    [property: JsonPropertyName("onefuzz_url")] Uri OneFuzzUrl,
    PoolName PoolName,
    Uri? HeartbeatQueue,
    string? InstanceTelemetryKey,
    string? MicrosoftTelemetryKey,
    string? MultiTenantDomain,
    Guid InstanceId
);



public record WorkSetSummary();
public record ScalesetSummary();

public record Vm(
    string Name,
    Region Region,
    string Sku,
    string Image,
    Authentication Auth,
    Nsg? Nsg,
    IDictionary<string, string>? Tags
) {
    public string Name { get; } = Name.Length > 40 ? throw new ArgumentOutOfRangeException("VM name too long") : Name;
};


public record SecretAddress(Uri Url);


/// This class allows us to store some data that are intended to be secret
/// The secret field stores either the raw data or the address of that data
/// This class allows us to maintain backward compatibility with existing
/// NotificationTemplate classes
public record SecretData<T>(T Secret) {
    public override string ToString() {
        if (Secret is SecretAddress) {
            if (Secret is null) {
                return string.Empty;
            } else {
                return Secret.ToString()!;
            }
        } else
            return "[REDACTED]";
    }
}

public record JobConfig(
    string Project,
    string Name,
    string Build,
    int Duration,
    string? Logs
);

public record JobTaskInfo(
    Guid TaskId,
    TaskType Type,
    TaskState State
);

public record Job(
    [PartitionKey] Guid JobId,
    JobState State,
    JobConfig Config,
    string? Error,
    DateTimeOffset? EndTime
) : StatefulEntityBase<JobState>(State) {
    public List<JobTaskInfo>? TaskInfo { get; set; }
    public UserInfo? UserInfo { get; set; }
}

public record Nsg(string Name, Region Region);

public record WorkUnit(
    Guid JobId,
    Guid TaskId,
    TaskType TaskType,
    TaskUnitConfig Config
);

public record VmDefinition(
    Compare Compare,
    int Value
);

public record TaskDefinition(
    TaskFeature[] Features,
    VmDefinition Vm,
    ContainerDefinition[] Containers,
    ContainerType? MonitorQueue = null
);

public record WorkSet(
    bool Reboot,
    Uri SetupUrl,
    bool Script,
    List<WorkUnit> WorkUnits
);





public record ContainerDefinition(
    ContainerType Type,
    Compare Compare,
    int Value,
    ContainerPermission Permissions);


// TODO: service shouldn't pass SyncedDir, but just the url and let the agent
// come up with paths
public record SyncedDir(string Path, Uri url);


public interface IContainerDef { }
public record SingleContainer(SyncedDir SyncedDir) : IContainerDef;
public record MultipleContainer(List<SyncedDir> SyncedDirs) : IContainerDef;


public record TaskUnitConfig(
    Guid InstanceId,
    Guid JobId,
    Guid TaskId,
    Uri logs,
    TaskType TaskType,
    string? InstanceTelemetryKey,
    string? MicrosoftTelemetryKey,
    Uri HeartbeatQueue
    ) {
    public Uri? inputQueue { get; set; }
    public String? SupervisorExe { get; set; }
    public Dictionary<string, string>? SupervisorEnv { get; set; }
    public List<string>? SupervisorOptions { get; set; }
    public string? SupervisorInputMarker { get; set; }
    public string? TargetExe { get; set; }
    public Dictionary<string, string>? TargetEnv { get; set; }
    public List<string>? TargetOptions { get; set; }
    public int? TargetTimeout { get; set; }
    public bool? TargetOptionsMerge { get; set; }
    public int? TargetWorkers { get; set; }
    public bool? CheckAsanLog { get; set; }
    public bool? CheckDebugger { get; set; }
    public int? CheckRetryCount { get; set; }
    public bool? CheckFuzzerHelp { get; set; }
    public bool? ExpectCrashOnFailure { get; set; }
    public bool? RenameOutput { get; set; }
    public string? GeneratorExe { get; set; }
    public Dictionary<string, string>? GeneratorEnv { get; set; }
    public List<string>? GeneratorOptions { get; set; }
    public ContainerType? WaitForFiles { get; set; }
    public string? AnalyzerExe { get; set; }
    public Dictionary<string, string>? AnalyzerEnv { get; set; }
    public List<string>? AnalyzerOptions { get; set; }
    public string? StatsFile { get; set; }
    public StatsFormat? StatsFormat { get; set; }
    public int? EnsembleSyncDelay { get; set; }
    public List<string>? ReportList { get; set; }
    public int? MinimizedStackDepth { get; set; }
    public string? CoverageFilter { get; set; }

    // from here forwards are Container definitions.  These need to be inline
    // with TaskDefinitions and ContainerTypes
    public IContainerDef? Analysis { get; set; }
    public IContainerDef? Coverage { get; set; }
    public IContainerDef? Crashes { get; set; }
    public IContainerDef? Inputs { get; set; }
    public IContainerDef? NoRepro { get; set; }
    public IContainerDef? ReadonlyInputs { get; set; }
    public IContainerDef? Reports { get; set; }
    public IContainerDef? Tools { get; set; }
    public IContainerDef? UniqueInputs { get; set; }
    public IContainerDef? UniqueReports { get; set; }
    public IContainerDef? RegressionReport { get; set; }

}
