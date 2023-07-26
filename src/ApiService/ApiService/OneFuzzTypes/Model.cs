using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Endpoint = System.String;
using GroupId = System.Guid;
using PrincipalId = System.Guid;

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
    HeartbeatData[] Data);

public record NodeHeartbeatEntry(Guid NodeId, HeartbeatData[] Data);

public record NodeCommandStopIfFree();

public record StopNodeCommand();

public record StopTaskNodeCommand(Guid TaskId);

public record NodeCommandAddSshKey(string PublicKey);

public record NodeCommand
(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    StopNodeCommand? Stop = default,

    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    StopTaskNodeCommand? StopTask = default,

    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    NodeCommandAddSshKey? AddSshKey = default,

    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    NodeCommandStopIfFree? StopIfFree = default
);

public enum NodeTaskState {
    Init,
    SettingUp,
    Running,
}

public record NodeTasks
(
    [PartitionKey] Guid MachineId,
    [RowKey] Guid TaskId,
    NodeTaskState State = NodeTaskState.Init
) : StatefulEntityBase<NodeTaskState>(State);

public record ProxyHeartbeat
(
    Region Region,
    Guid ProxyId,
    List<Forward> Forwards,
    DateTimeOffset TimeStamp
) {
    public override string ToString() {
        return JsonSerializer.Serialize(this);
    }
};

public record Node
(
    [PartitionKey] PoolName PoolName,
    [RowKey] Guid MachineId,
    Guid? PoolId,
    string Version,
    DateTimeOffset? Heartbeat = null,
    DateTimeOffset? InitializedAt = null,
    NodeState State = NodeState.Init,
    Os? Os = null,

    // InstanceId is always numeric, but the APIs
    // deal with it as a string, so we keep it as
    // a string internally.
    string? InstanceId = null,

    ScalesetId? ScalesetId = null,

    bool ReimageRequested = false,
    bool DeleteRequested = false,
    bool DebugKeepNode = false,
    bool Managed = true
) : StatefulEntityBase<NodeState>(State) { }


public record Forward
(
    long SrcPort,
    long DstPort,
    string DstIp
);


public record ProxyForward
(
    [PartitionKey] Region Region,
    [RowKey] long Port,
    ScalesetId ScalesetId,
    Guid MachineId,
    Guid? ProxyId,
    long DstPort,
    string DstIp,
    [property: JsonPropertyName("endtime")] DateTimeOffset EndTime
) : EntityBase();

public record ProxyConfig
(
    Uri Url,
    Uri Notification,
    Region Region,
    Guid? ProxyId,
    List<Forward> Forwards,
    string InstanceTelemetryKey,
    string? MicrosoftTelemetryKey,
    Guid InstanceId
);

public record Proxy
(
    [PartitionKey] Region Region,
    [RowKey] Guid ProxyId,
    DateTimeOffset? CreatedTimestamp,
    VmState State,
    ISecret<Authentication> Auth,
    string? Ip,
    Error? Error,
    string Version,
    ProxyHeartbeat? Heartbeat,
    bool Outdated
) : StatefulEntityBase<VmState>(State);

public record Error(ErrorCode Code, List<string>? Errors) {
    // A human-readable version of the ErrorCode,
    // so that when serialized to JSON there is something useful,
    // not just a number. This is named 'Title' to align with the
    // ProblemDetails class.
    public string Title => Code.ToString();

    public static Error Create(ErrorCode code, params string[] errors)
        => new(code, errors.ToList());

    public sealed override string ToString() {
        var errorsString = Errors != null ? string.Join("; ", Errors) : string.Empty;
        return $"Error {{ Code = {Code}, Errors = {errorsString} }}";
    }
};


public record UserInfo(Guid? ApplicationId, Guid? ObjectId, String? Upn) {
}

public record TaskDetails(
    TaskType Type,
    long Duration,
    string? TargetExe = null,
    Dictionary<string, string>? TargetEnv = null,
    List<string>? TargetOptions = null,
    long? TargetWorkers = null,
    bool? TargetOptionsMerge = null,
    bool? CheckAsanLog = null,
    bool? CheckDebugger = null,
    long? CheckRetryCount = null,
    bool? CheckFuzzerHelp = null,
    bool? ExpectCrashOnFailure = null,
    bool? RenameOutput = null,
    string? SupervisorExe = null,
    Dictionary<string, string>? SupervisorEnv = null,
    List<string>? SupervisorOptions = null,
    string? SupervisorInputMarker = null,
    string? GeneratorExe = null,
    Dictionary<string, string>? GeneratorEnv = null,
    List<string>? GeneratorOptions = null,
    string? AnalyzerExe = null,
    Dictionary<string, string>? AnalyzerEnv = null,
    List<string>? AnalyzerOptions = null,
    ContainerType? WaitForFiles = null,
    string? StatsFile = null,
    StatsFormat? StatsFormat = null,
    bool? RebootAfterSetup = null,
    long? TargetTimeout = null,
    long? EnsembleSyncDelay = null,
    bool? PreserveExistingOutputs = null,
    List<string>? ReportList = null,
    long? MinimizedStackDepth = null,
    Dictionary<string, string>? TaskEnv = null,

    // Deprecated. Retained for processing old table data.
    string? CoverageFilter = null,

    string? ModuleAllowlist = null,
    string? SourceAllowlist = null,
    string? TargetAssembly = null,
    string? TargetClass = null,
    string? TargetMethod = null
);

public record TaskVm(
    Region Region,
    string Sku,
    ImageReference Image,
    bool? RebootAfterSetup,
    long Count = 1,
    bool SpotInstance = false
);

public record TaskPool(
    long Count,
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
   TaskVm? Vm = null,
   TaskPool? Pool = null,
   List<TaskContainers>? Containers = null,
   Dictionary<string, string>? Tags = null,
   List<TaskDebugFlag>? Debug = null,
   bool? Colocate = null
);

public record TaskEventSummary(
    DateTimeOffset? Timestamp,
    string EventData,
    string EventType
);


public record NodeAssignment(
    Guid NodeId,
    ScalesetId? ScalesetId,
    NodeTaskState State
);


public record Task(
    [PartitionKey] Guid JobId,
    [RowKey] Guid TaskId,
    TaskState State,
    Os Os,
    TaskConfig Config,
    Error? Error = null,
    ISecret<Authentication>? Auth = null,
    DateTimeOffset? Heartbeat = null,
    DateTimeOffset? EndTime = null,
    StoredUserInfo? UserInfo = null) : StatefulEntityBase<TaskState>(State), IJobTaskInfo {
    public TaskType Type => Config.Task.Type;
}

public record TaskEvent(
    [PartitionKey, RowKey] Guid TaskId,
    Guid MachineId,
    WorkerEvent EventData
) : EntityBase;

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
    IReadOnlyList<string> Methods,
    IReadOnlyList<Guid> AllowedGroups
);

//# initial set of admins can only be set during deployment.
//# if admins are set, only admins can update instance configs.
//# if set, only admins can manage pools or scalesets
public record InstanceConfig
(
    [PartitionKey, RowKey] string InstanceName,
    Guid[]? Admins,

    string[] AllowedAadTenants,
    [DefaultValue(InitMethod.DefaultConstructor)] NetworkConfig NetworkConfig,
    [DefaultValue(InitMethod.DefaultConstructor)] NetworkSecurityGroupConfig ProxyNsgConfig,
    AzureVmExtensionConfig? Extensions = null,
    ImageReference? DefaultWindowsVmImage = null,
    ImageReference? DefaultLinuxVmImage = null,
    string ProxyVmSku = "Standard_B2s",
    bool RequireAdminPrivileges = false,
    IDictionary<Endpoint, ApiAccessRule>? ApiAccessRules = null,
    IDictionary<PrincipalId, GroupId[]>? GroupMembership = null,
    IDictionary<string, string>? VmTags = null,
    IDictionary<string, string>? VmssTags = null
) : EntityBase() {

    public InstanceConfig(string instanceName) : this(
        InstanceName: instanceName,
        Admins: null,
        AllowedAadTenants: Array.Empty<string>(),
        NetworkConfig: new NetworkConfig(),
        ProxyNsgConfig: new NetworkSecurityGroupConfig()) { }

    public static List<Guid>? CheckAdmins(List<Guid>? value) {
        if (value is not null && value.Count == 0) {
            throw new ArgumentException("admins must be null or contain at least one UUID");
        } else {
            return value;
        }
    }

    public InstanceConfig() : this(String.Empty) { }

    // At the moment, this only checks allowed_aad_tenants, however adding
    // support for 3rd party JWT validation is anticipated in a future release.
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

public record AutoScale(
    [PartitionKey, RowKey] ScalesetId ScalesetId,
    long Min,
    long Max,
    long Default,
    long ScaleOutAmount,
    long ScaleOutCooldown,
    long ScaleInAmount,
    long ScaleInCooldown
) : EntityBase;


public partial record Scaleset(
    [PartitionKey] PoolName PoolName,
    [RowKey] ScalesetId ScalesetId,
    ScalesetState State,
    string VmSku,
    ImageReference Image,
    Region Region,
    long Size,
    bool? SpotInstances,
    bool EphemeralOsDisks,
    bool NeedsConfigUpdate,
    Dictionary<string, string> Tags,
    ISecret<Authentication>? Auth = null,
    Error? Error = null,
    Guid? ClientId = null,
    Guid? ClientObjectId = null
// 'Nodes' removed when porting from Python: only used in search response
) : StatefulEntityBase<ScalesetState>(State) {

    [GeneratedRegex(@"[^a-zA-Z0-9\-]+")]
    private static partial Regex InvalidCharacterRegex();

    public static ScalesetId GenerateNewScalesetId(PoolName poolName)
        => GenerateNewScalesetIdUsingGuid(poolName, Guid.NewGuid());

    public static ScalesetId GenerateNewScalesetIdUsingGuid(PoolName poolName, Guid guid) {
        // poolnames permit underscores but not scaleset names; use hyphen instead:
        var name = poolName.ToString().Replace("_", "-");

        // since poolnames are not actually validated, take only the valid characters:
        name = InvalidCharacterRegex().Replace(name, "");

        // trim off any starting and ending dashes:
        name = name.Trim('-');

        // this should now be a valid name; generate a unique suffix:
        // max length is 64; length of Guid in "N" format is 32, -1 for the hyphen
        name = name[..Math.Min(64 - 32 - 1, name.Length)] + "-" + guid.ToString("N");

        return ScalesetId.Parse(name);
    }
}

public record Notification(
    [PartitionKey] Guid NotificationId,
    [RowKey] Container Container,
    NotificationTemplate Config
) : EntityBase();

public record BlobRef(
    string Account,
    Container Container,
    string Name
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
    long? ScarinessScore,
    string? ScarinessDescription,
    List<string>? MinimizedStack,
    string? MinimizedStackSha256,
    List<string>? MinimizedStackFunctionNames,
    string? MinimizedStackFunctionNamesSha256,
    List<string>? MinimizedStackFunctionLines,
    string? MinimizedStackFunctionLinesSha256,
    string? ToolName,
    string? ToolVersion,
    string? OnefuzzVersion,
    Uri? ReportUrl
) : IReport, ITruncatable<Report> {

    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    public Report Truncate(int maxLength) {
        return this with {
            Executable = TruncateUtils.TruncateString(Executable, maxLength),
            CrashType = TruncateUtils.TruncateString(CrashType, maxLength),
            CrashSite = TruncateUtils.TruncateString(CrashSite, maxLength),
            CallStack = TruncateUtils.TruncateList(CallStack, maxLength),
            CallStackSha256 = TruncateUtils.TruncateString(CallStackSha256, maxLength),
            InputSha256 = TruncateUtils.TruncateString(InputSha256, maxLength),
            AsanLog = TruncateUtils.TruncateString(AsanLog, maxLength),
            ScarinessDescription = TruncateUtils.TruncateString(ScarinessDescription, maxLength),
            MinimizedStack = MinimizedStack != null ? TruncateUtils.TruncateList(MinimizedStack, maxLength) : MinimizedStack,
            MinimizedStackSha256 = TruncateUtils.TruncateString(MinimizedStackSha256, maxLength),
            MinimizedStackFunctionNames = MinimizedStackFunctionNames != null ? TruncateUtils.TruncateList(MinimizedStackFunctionNames, maxLength) : MinimizedStackFunctionNames,
            MinimizedStackFunctionNamesSha256 = TruncateUtils.TruncateString(MinimizedStackFunctionNamesSha256, maxLength),
            MinimizedStackFunctionLines = MinimizedStackFunctionLines != null ? TruncateUtils.TruncateList(MinimizedStackFunctionLines, maxLength) : MinimizedStackFunctionLines,
            MinimizedStackFunctionLinesSha256 = TruncateUtils.TruncateString(MinimizedStackFunctionLinesSha256, maxLength),
            ToolName = TruncateUtils.TruncateString(ToolName, maxLength),
            ToolVersion = TruncateUtils.TruncateString(ToolVersion, maxLength),
            OnefuzzVersion = TruncateUtils.TruncateString(OnefuzzVersion, maxLength),
        };
    }
}

public record NoReproReport(
    string InputSha,
    BlobRef? InputBlob,
    string? Executable,
    Guid TaskId,
    Guid JobId,
    long Tries,
    string? Error
) : ITruncatable<NoReproReport> {
    public NoReproReport Truncate(int maxLength) {
        return this with {
            Executable = Executable?[..maxLength],
            Error = Error?[..maxLength]
        };
    }
}

public record CrashTestResult(
    Report? CrashReport,
    NoReproReport? NoReproReport
) : ITruncatable<CrashTestResult> {
    public CrashTestResult Truncate(int maxLength) {
        return new CrashTestResult(
            CrashReport?.Truncate(maxLength),
            NoReproReport?.Truncate(maxLength)
        );
    }
}

public record RegressionReport(
    CrashTestResult CrashTestResult,
    CrashTestResult? OriginalCrashTestResult,
    Uri? ReportUrl
) : IReport, ITruncatable<RegressionReport> {
    public RegressionReport Truncate(int maxLength) {
        return new RegressionReport(
            CrashTestResult.Truncate(maxLength),
            OriginalCrashTestResult?.Truncate(maxLength),
            ReportUrl
        );
    }
}

public record UnknownReportType(
    Uri? ReportUrl
) : IReport;

[JsonConverter(typeof(NotificationTemplateConverter))]
#pragma warning disable CA1715
public interface NotificationTemplate {
#pragma warning restore CA1715
    Async.Task<OneFuzzResultVoid> Validate();
}


public class NotificationTemplateConverter : JsonConverter<NotificationTemplate> {
    public override NotificationTemplate? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using var templateJson = JsonDocument.ParseValue(ref reader);
        try {
            return ValidateDeserialization(templateJson.Deserialize<AdoTemplate>(options));
        } catch (Exception ex) when (
              ex is JsonException
              || ex is ArgumentNullException
              || ex is ArgumentOutOfRangeException
          ) {

        }

        try {
            return ValidateDeserialization(templateJson.Deserialize<TeamsTemplate>(options));
        } catch (Exception ex) when (
              ex is JsonException
              || ex is ArgumentNullException
              || ex is ArgumentOutOfRangeException
          ) {

        }

        try {
            return ValidateDeserialization(templateJson.Deserialize<GithubIssuesTemplate>(options));
        } catch (Exception ex) when (
              ex is JsonException
              || ex is ArgumentNullException
              || ex is ArgumentOutOfRangeException
          ) {

        }

        var expectedTemplateTypes = new List<Type> {
            typeof(AdoTemplate),
            typeof(TeamsTemplate),
            typeof(GithubIssuesTemplate)
        }
        .Select(type => type.ToString());

        throw new JsonException($"Unsupported notification template. Could not deserialize {templateJson} into one of the following template types: {string.Join(", ", expectedTemplateTypes)}");
    }

    public override void Write(Utf8JsonWriter writer, NotificationTemplate value, JsonSerializerOptions options) {
        if (value is AdoTemplate adoTemplate) {
            JsonSerializer.Serialize(writer, adoTemplate, options);
        } else if (value is TeamsTemplate teamsTemplate) {
            JsonSerializer.Serialize(writer, teamsTemplate, options);
        } else if (value is GithubIssuesTemplate githubIssuesTemplate) {
            JsonSerializer.Serialize(writer, githubIssuesTemplate, options);
        } else {
            throw new JsonException("Unsupported notification template");
        }

    }

    private static T ValidateDeserialization<T>(T? obj) {
        if (obj == null) {
            throw new ArgumentNullException($"Failed to deserialize type: {typeof(T)}. It was null.");
        }
        var nonNullableParameters = obj.GetType().GetConstructors().First().GetParameters()
            .Where(parameter => !parameter.HasDefaultValue)
            .Select(parameter => parameter.Name)
            .Where(pName => pName != null)
            .ToHashSet();

        var nullProperties = obj.GetType().GetProperties()
            .Where(property => property.GetValue(obj) == null)
            .Select(property => property.Name)
            .ToHashSet<Endpoint>();

        var nullNonNullableProperties = nonNullableParameters.Intersect(nullProperties);

        if (nullNonNullableProperties.Any()) {
            throw new ArgumentOutOfRangeException($"Failed to deserialize type: {obj.GetType()}. The following non nullable properties are missing values: {string.Join(", ", nullNonNullableProperties)}");
        }

        return obj;
    }
}


public record ADODuplicateTemplate(
    List<string> Increment,
    Dictionary<string, string> SetState,
    Dictionary<string, string> AdoFields,
    string? Comment = null,
    List<Dictionary<string, string>>? Unless = null
);

public record AdoTemplate(
    Uri BaseUrl,
    SecretData<string> AuthToken,
    string Project,
    string Type,
    List<string> UniqueFields,
    Dictionary<string, string> AdoFields,
    ADODuplicateTemplate OnDuplicate,
    string? Comment = null
    ) : NotificationTemplate {
    public async Task<OneFuzzResultVoid> Validate() {
        return await Ado.Validate(this);
    }
}

public record TeamsTemplate(SecretData<string> Url) : NotificationTemplate {
    public Task<OneFuzzResultVoid> Validate() {
        // The only way we can validate in the current state is to send a test webhook
        // Maybe there's a teams nuget package we can pull in to help validate
        return Async.Task.FromResult(OneFuzzResultVoid.Ok);
    }
}

public record GithubAuth(string User, string PersonalAccessToken);

public record GithubIssueSearch(
    List<GithubIssueSearchMatch> FieldMatch,
    [property: JsonPropertyName("string")] String str,
    string? Author = null,
    GithubIssueState? State = null
);

public record GithubIssueDuplicate(
    List<string> Labels,
    bool Reopen,
    string? Comment = null
);


public record GithubIssuesTemplate(
    SecretData<GithubAuth> Auth,
    string Organization,
    string Repository,
    string Title,
    string Body,
    GithubIssueSearch UniqueSearch,
    List<string> Assignees,
    List<string> Labels,
    GithubIssueDuplicate OnDuplicate
    ) : NotificationTemplate {
    public async Task<OneFuzzResultVoid> Validate() {
        return await GithubIssues.Validate(this);
    }
}

public record Repro(
    [PartitionKey][RowKey] Guid VmId,
    Guid TaskId,
    ReproConfig Config,
    ISecret<Authentication> Auth,
    Os Os,
    VmState State = VmState.Init,
    Error? Error = null,
    string? Ip = null,
    DateTimeOffset? EndTime = null,
    StoredUserInfo? UserInfo = null
) : StatefulEntityBase<VmState>(State);

// TODO: Make this >1 and < 7*24 (more than one hour, less than seven days)
public record ReproConfig(
    Container Container,
    string Path,
    long Duration
);

// Skipping AutoScaleConfig because it's not used anymore
public record Pool(
    [PartitionKey] PoolName Name,
    [RowKey] Guid PoolId,
    Os Os,
    bool Managed,
    Architecture Arch,
    PoolState State,
    Guid? ObjectId = null
) : StatefulEntityBase<PoolState>(State) {
    public List<Node>? Nodes { get; set; }
    public AgentConfig? Config { get; set; }
}

public record WorkUnitSummary(
    Guid JobId,
    Guid TaskId,
    TaskType TaskType
);

public record WorkSetSummary(
    List<WorkUnitSummary> WorkUnits
);

public record ScalesetSummary(
    ScalesetId ScalesetId,
    ScalesetState State
);

public record ClientCredentials
(
    Guid ClientId,
    string ClientSecret
);

public record ContainerInformation(
    [PartitionKey] StorageType Type,
    [RowKey] Container Name,
    string ResourceId // full ARM resource ID for the container
) : EntityBase;

public record AgentConfig(
    ClientCredentials? ClientCredentials,
    [property: JsonPropertyName("onefuzz_url")] Uri OneFuzzUrl,
    PoolName PoolName,
    Uri? HeartbeatQueue,
    string? InstanceTelemetryKey,
    string? MicrosoftTelemetryKey,
    string? MultiTenantDomain,
    Guid InstanceId,
    bool? Managed = true
);


public record Vm(
    string Name,
    Region Region,
    string Sku,
    ImageReference Image,
    ISecret<Authentication> Auth,
    Nsg? Nsg,
    IDictionary<string, string>? Tags
) {
    public string Name { get; } = Name.Length > 40 ? throw new ArgumentOutOfRangeException("VM name too long") : Name;
};


public interface ISecret {
    [JsonIgnore]
    bool IsHIddden { get; }
    [JsonIgnore]
    Uri? Uri { get; }
    string? GetValue();
}
[JsonConverter(typeof(ISecretConverterFactory))]
public interface ISecret<T> : ISecret { }

public class ISecretConverterFactory : JsonConverterFactory {
    public override bool CanConvert(Type typeToConvert) {
        return typeToConvert.IsGenericType && typeToConvert.Name == typeof(ISecret<string>).Name;
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) {
        var innerType = typeToConvert.GetGenericArguments().First();
        return (JsonConverter)Activator.CreateInstance(
            typeof(ISecretConverter<>).MakeGenericType(innerType),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            args: Array.Empty<object?>(),
            culture: null)!;
    }
}

public class ISecretConverter<T> : JsonConverter<ISecret<T>> {
    public override ISecret<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {

        using var secretJson = JsonDocument.ParseValue(ref reader);

        if (secretJson.RootElement.ValueKind == JsonValueKind.String) {
            return (ISecret<T>)new SecretValue<string>(secretJson.RootElement.GetString()!);
        }

        if (secretJson.RootElement.TryGetProperty("url", out var secretUrl)) {
            return new SecretAddress<T>(new Uri(secretUrl.GetString()!));
        }

        return new SecretValue<T>(secretJson.Deserialize<T>(options)!);
    }

    public override void Write(Utf8JsonWriter writer, ISecret<T> value, JsonSerializerOptions options) {
        if (value is SecretAddress<T> secretAddress) {
            JsonSerializer.Serialize(writer, secretAddress, options);
        } else if (value is SecretValue<T> secretValue) {
            throw new JsonException("SecretValue should not be serialized");
        }
    }
}



public record SecretValue<T>(T Value) : ISecret<T> {
    [JsonIgnore]
    public bool IsHIddden => false;
    [JsonIgnore]
    public Uri? Uri => null;

    public string? GetValue() {
        if (Value is string secretString) {
            return secretString.Trim();
        }

        return JsonSerializer.Serialize(Value, EntityConverter.GetJsonSerializerOptions());
    }
}

public record SecretAddress<T>(Uri Url) : ISecret<T> {
    [JsonIgnore]
    public Uri? Uri => Url;
    [JsonIgnore]
    public bool IsHIddden => true;
    public string? GetValue() => null;


}

public record SecretData<T>(ISecret<T> Secret) {
}

public record JobConfig(
    string Project,
    string Name,
    string Build,
    long Duration,
    string? Logs
) : ITruncatable<JobConfig> {
    public JobConfig Truncate(int maxLength) {
        return new JobConfig(
            Project,
            Name,
            Build,
            Duration,
            Logs?[..maxLength]
        );
    }
}

[JsonDerivedType(typeof(Task), typeDiscriminator: "Task")]
[JsonDerivedType(typeof(JobTaskInfo), typeDiscriminator: "JobTaskInfo")]
public interface IJobTaskInfo {
    Guid TaskId { get; }
    TaskType Type { get; }
    TaskState State { get; }
}

public record JobTaskInfo(
    Guid TaskId,
    TaskType Type,
    TaskState State
) : IJobTaskInfo;

public record Job(
    [PartitionKey][RowKey] Guid JobId,
    JobState State,
    JobConfig Config,
    StoredUserInfo? UserInfo,
    string? Error = null,
    DateTimeOffset? EndTime = null
) : StatefulEntityBase<JobState>(State) { }

// This is like UserInfo but lacks the UPN:
public record StoredUserInfo(Guid? ApplicationId, Guid? ObjectId);

public record Nsg(string Name, Region Region) {
    public static Nsg ForRegion(Region region)
        => new(NameFromRegion(region), region);

    // Currently, the name of a NSG is the same as the region it is in.
    public static string NameFromRegion(Region region)
        => region.String;
};

public record WorkUnit(
    Guid JobId,
    Guid TaskId,
    TaskType TaskType,
    Dictionary<string, string> Env,
    // JSON-serialized `TaskUnitConfig`.
    [property: JsonConverter(typeof(TaskUnitConfigConverter))] TaskUnitConfig Config
);

public class TaskUnitConfigConverter : JsonConverter<TaskUnitConfig> {
    public override TaskUnitConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        var taskUnitString = reader.GetString();
        if (taskUnitString == null) {
            return null;
        }
        return JsonSerializer.Deserialize<TaskUnitConfig>(taskUnitString, options);
    }

    public override void Write(Utf8JsonWriter writer, TaskUnitConfig value, JsonSerializerOptions options) {
        var v = JsonSerializer.Serialize(value, new JsonSerializerOptions(options) {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        writer.WriteStringValue(v);
    }
}

public record VmDefinition(
    Compare Compare,
    long Value
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
    Uri? ExtraSetupUrl,
    bool Script,
    List<WorkUnit> WorkUnits
);

public readonly record struct ContainerDefinition(
    ContainerType Type,
    Compare Compare,
    long Value,
    ContainerPermission Permissions);

// TODO: service shouldn't pass SyncedDir, but just the url and let the agent
// come up with paths
public readonly record struct SyncedDir(string Path, Uri Url);

[JsonConverter(typeof(ContainerDefConverter))]
public interface IContainerDef { }
public record SingleContainer(SyncedDir SyncedDir) : IContainerDef;
public record MultipleContainer(IReadOnlyList<SyncedDir> SyncedDirs) : IContainerDef;


public class ContainerDefConverter : JsonConverter<IContainerDef> {
    public override IContainerDef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.StartObject) {
            var result = (SyncedDir?)JsonSerializer.Deserialize(ref reader, typeof(SyncedDir), options);
            if (result is SyncedDir sd) {
                return new SingleContainer(sd);
            }

            return null;
        }

        if (reader.TokenType == JsonTokenType.StartArray) {
            var result = (List<SyncedDir>?)JsonSerializer.Deserialize(ref reader, typeof(List<SyncedDir>), options);
            if (result is null) {
                return null;
            }

            return new MultipleContainer(result);
        }

        throw new JsonException("expecting array or object");
    }

    public override void Write(Utf8JsonWriter writer, IContainerDef value, JsonSerializerOptions options) {
        switch (value) {
            case SingleContainer container:
                JsonSerializer.Serialize(writer, container.SyncedDir, options);
                break;
            case MultipleContainer { SyncedDirs: var syncedDirs }:
                JsonSerializer.Serialize(writer, syncedDirs, options);
                break;
            default:
                throw new NotSupportedException($"invalid IContainerDef type: {value.GetType()}");
        }
    }
}



public record TaskUnitConfig(
    Guid InstanceId,
    Guid JobId,
    Guid TaskId,
    Uri logs,
    TaskType TaskType,
    string? InstanceTelemetryKey,
    string? MicrosoftTelemetryKey,
    Uri HeartbeatQueue,
    Dictionary<string, string> Tags
    ) {
    public Uri? inputQueue { get; set; }
    public String? SupervisorExe { get; set; }
    public Dictionary<string, string>? SupervisorEnv { get; set; }
    public List<string>? SupervisorOptions { get; set; }
    public string? SupervisorInputMarker { get; set; }
    public string? TargetExe { get; set; }
    public Dictionary<string, string>? TargetEnv { get; set; }
    public List<string>? TargetOptions { get; set; }
    public long? TargetTimeout { get; set; }
    public bool? TargetOptionsMerge { get; set; }
    public long? TargetWorkers { get; set; }
    public bool? CheckAsanLog { get; set; }
    public bool? CheckDebugger { get; set; }
    public long? CheckRetryCount { get; set; }
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
    public long? EnsembleSyncDelay { get; set; }
    public List<string>? ReportList { get; set; }
    public long? MinimizedStackDepth { get; set; }

    // Deprecated. Retained for processing old table data.
    public string? CoverageFilter { get; set; }

    public bool? PreserveExistingOutputs { get; set; }
    public string? ModuleAllowlist { get; set; }
    public string? SourceAllowlist { get; set; }
    public string? TargetAssembly { get; set; }
    public string? TargetClass { get; set; }
    public string? TargetMethod { get; set; }

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
    public IContainerDef? RegressionReports { get; set; }
    public IContainerDef? ExtraSetup { get; set; }
    public IContainerDef? ExtraOutput { get; set; }
}

public record NodeCommandEnvelope(
    NodeCommand Command,
    string MessageId
);

public record TemplateRenderContext(
    Report Report,
    TaskConfig Task,
    JobConfig Job,
    Uri ReportUrl,
    Uri InputUrl,
    Uri TargetUrl,
    Container ReportContainer,
    string ReportFilename,
    string IssueTitle,
    string ReproCmd
);

public interface ITruncatable<T> {
    public T Truncate(int maxLength);
}
