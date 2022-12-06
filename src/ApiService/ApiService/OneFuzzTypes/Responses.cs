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
    Guid? ScalesetId,
    bool ReimageRequested,
    bool DeleteRequested,
    bool DebugKeepNode
) : BaseResponse();

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
    UserInfo? UserInfo,
    List<TaskEventSummary> Events,
    List<NodeAssignment> Nodes
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
    List<JobTaskInfo>? TaskInfo
// not including UserInfo from Job model
) : BaseResponse() {
    public static JobResponse ForJob(Job j)
        => new(
            JobId: j.JobId,
            State: j.State,
            Config: j.Config,
            Error: j.Error,
            EndTime: j.EndTime,
            TaskInfo: j.TaskInfo
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
    Guid ScalesetId,
    ScalesetState State,
    Authentication? Auth,
    string VmSku,
    string Image,
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
    public static ScalesetResponse ForScaleset(Scaleset s, bool includeAuth)
        => new(
            PoolName: s.PoolName,
            ScalesetId: s.ScalesetId,
            State: s.State,
            Auth: includeAuth ? s.Auth : null,
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
