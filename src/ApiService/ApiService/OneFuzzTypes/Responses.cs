using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.OneFuzz.Service;

[JsonConverter(typeof(BaseResponseConverter))]
public abstract record BaseResponse();

public record CanSchedule(
    bool Allowed,
    bool WorkStopped
) : BaseResponse();

public record PendingNodeCommand(
    NodeCommandEnvelope? Envelope
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

public class BaseResponseConverter : JsonConverter<BaseResponse> {
    public override BaseResponse? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        return null;
    }

    public override void Write(Utf8JsonWriter writer, BaseResponse value, JsonSerializerOptions options) {
        var eventType = value.GetType();
        JsonSerializer.Serialize(writer, value, eventType, options);
    }
}
