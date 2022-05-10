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


public class BaseResponseConverter : JsonConverter<BaseResponse> {
    public override BaseResponse? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        return null;
    }

    public override void Write(Utf8JsonWriter writer, BaseResponse value, JsonSerializerOptions options) {
        var eventType = value.GetType();
        JsonSerializer.Serialize(writer, value, eventType, options);
    }
}
