using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.OneFuzz.Service;

[JsonConverter(typeof(BaseResponseConverter))]
public abstract record BaseResponse() {
    public static implicit operator BaseResponse(bool value)
        => new BoolResult(value);
};

public record CanSchedule(
    bool Allowed,
    bool WorkStopped,
    string? Reason
) : BaseResponse();

public record PendingNodeCommand(
    NodeCommandEnvelope? Envelope
) : BaseResponse();

// TODO: not sure how much of this is actually
// needed in the search results, so at the moment
// it is a copy of the whole Node type
public record NodeSearchResult(
    PoolName PoolName,
    Guid MachineId,
    Guid? PoolId,
    string Version,
    DateTimeOffset? Heartbeat,
    DateTimeOffset? InitializedAt,
    NodeState State,
    ScalesetId? ScalesetId,
    bool ReimageRequested,
    bool DeleteRequested,
    bool DebugKeepNode,
    List<NodeTasks>? Tasks,
    List<NodeCommand>? Messages) : BaseResponse();

public record TaskSearchResult(
     Guid JobId,
     Guid TaskId,
    TaskState State,
    Os Os,
    TaskConfig Config,
    Error? Error,
    Authentication? Auth,
    DateTimeOffset? Heartbeat,
    DateTimeOffset? EndTime,
    StoredUserInfo? UserInfo,
    List<TaskEventSummary> Events,
    List<NodeAssignment> Nodes,
    [property: JsonPropertyName("Timestamp")] // must retain capital T for backcompat
    DateTimeOffset? Timestamp
) : BaseResponse();

public record BoolResult(
    bool Result
) : BaseResponse();

public record InfoResponse(
    string ResourceGroup,
    Region Region,
    string Subscription,
    IReadOnlyDictionary<string, InfoVersion> Versions,
    Guid? InstanceId,
    string? InsightsAppid,
    string? InsightsInstrumentationKey
) : BaseResponse();

public record InfoVersion(
    string Git,
    string Build,
    string Version);

public record AgentRegistrationResponse(
    Uri EventsUrl,
    Uri WorkQueue,
    Uri CommandsUrl
) : BaseResponse();

public record ContainerInfoBase(
    Container Name,
    IDictionary<string, string>? Metadata
) : BaseResponse();

public record ContainerInfo(
    Container Name,
    IDictionary<string, string>? Metadata,
    Uri SasUrl
) : BaseResponse();

public record JobResponse(
    Guid JobId,
    JobState State,
    JobConfig Config,
    string? Error,
    DateTimeOffset? EndTime,
    IEnumerable<IJobTaskInfo>? TaskInfo,
    StoredUserInfo? UserInfo,
    [property: JsonPropertyName("Timestamp")] // must retain capital T for backcompat
    DateTimeOffset? Timestamp
// not including UserInfo from Job model
) : BaseResponse() {
    public static JobResponse ForJob(Job j, IEnumerable<IJobTaskInfo>? taskInfo)
        => new(
            JobId: j.JobId,
            State: j.State,
            Config: j.Config,
            Error: j.Error,
            EndTime: j.EndTime,
            TaskInfo: taskInfo,
            UserInfo: j.UserInfo,
            Timestamp: j.Timestamp
        );
}

public record PoolGetResult(
    PoolName Name,
    Guid PoolId,
    Os Os,
    bool Managed,
    Architecture Arch,
    PoolState State,
    Guid? ObjectId,
    List<Node>? Nodes,
    AgentConfig? Config,
    List<WorkSetSummary>? WorkQueue,
    List<ScalesetSummary>? ScalesetSummary
) : BaseResponse();

public record ScalesetResponse(
    PoolName PoolName,
    ScalesetId ScalesetId,
    ScalesetState State,
    Authentication? Auth,
    string VmSku,
    ImageReference Image,
    Region Region,
    long Size,
    bool? SpotInstances,
    bool EmphemeralOsDisks,
    bool NeedsConfigUpdate,
    Error? Error,
    Guid? ClientId,
    Guid? ClientObjectId,
    Dictionary<string, string> Tags,
    List<ScalesetNodeState>? Nodes
) : BaseResponse() {
    public static ScalesetResponse ForScaleset(Scaleset s, Authentication? auth = null)
        => new(
            PoolName: s.PoolName,
            ScalesetId: s.ScalesetId,
            State: s.State,
            Auth: auth,
            VmSku: s.VmSku,
            Image: s.Image,
            Region: s.Region,
            Size: s.Size,
            SpotInstances: s.SpotInstances,
            EmphemeralOsDisks: s.EphemeralOsDisks,
            NeedsConfigUpdate: s.NeedsConfigUpdate,
            Error: s.Error,
            ClientId: s.ClientId,
            ClientObjectId: s.ClientObjectId,
            Tags: s.Tags,
            Nodes: null);
}

public record ScalesetNodeState(
    Guid MachineId,
    string? InstanceId,
    NodeState? State
);

public record ConfigResponse(
    string? Authority,
    string? ClientId,
    string? TenantDomain,
    string? MultiTenantDomain
) : BaseResponse();

public class BaseResponseConverter : JsonConverter<BaseResponse> {
    public override BaseResponse? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        return null;
    }

    public override void Write(Utf8JsonWriter writer, BaseResponse value, JsonSerializerOptions options) {
        var eventType = value.GetType();
        JsonSerializer.Serialize(writer, value, eventType, options);
    }
}

public record ProxyGetResult(
    string? Ip,
    Forward Forward
);

public record ProxyInfo(
    Region Region,
    Guid ProxyId,
    VmState State
);

public record ProxyList(
    List<ProxyInfo> Proxies
);

public record TemplateValidationResponse(
    string RenderedTemplate,
    TemplateRenderContext AvailableContext
) : BaseResponse();

public record JinjaToScribanMigrationResponse(
    List<Guid> UpdatedNotificationIds,
    List<Guid> FailedNotificationIds
) : BaseResponse();

public record JinjaToScribanMigrationDryRunResponse(
    List<Guid> NotificationIdsToUpdate
) : BaseResponse();

public record EventGetResponse(
    DownloadableEventMessage Event
) : BaseResponse();

public record NotificationTestResponse(
    bool Success,
    string? Error = null
) : BaseResponse();


public record ReproVmResponse(
    Guid VmId,
    Guid TaskId,
    ReproConfig Config,
    Authentication? Auth,
    Os Os,
    VmState State = VmState.Init,
    Error? Error = null,
    string? Ip = null,
    DateTimeOffset? EndTime = null,
    StoredUserInfo? UserInfo = null
) : BaseResponse() {

    public static ReproVmResponse FromRepro(Repro repro, Authentication? auth) {
        return new ReproVmResponse(
            VmId: repro.VmId,
            TaskId: repro.TaskId,
            Config: repro.Config,
            Auth: auth,
            Os: repro.Os,
            State: repro.State,
            Error: repro.Error,
            Ip: repro.Ip,
            EndTime: repro.EndTime,
            UserInfo: repro.UserInfo
        );
    }
}
