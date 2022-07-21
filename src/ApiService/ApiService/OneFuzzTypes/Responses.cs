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
    bool WorkStopped
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

public record BoolResult(
    bool Result
) : BaseResponse();

public record InfoResponse(
    string ResourceGroup,
    string Region,
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
    Guid? ClientId,
    List<Node>? Nodes,
    AgentConfig? Config,
    List<WorkSetSummary>? WorkQueue,
    List<ScalesetSummary>? ScalesetSummary
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
    string Region,
    Guid ProxyId,
    VmState State
);

public record ProxyList(
    List<ProxyInfo> Proxies
);

