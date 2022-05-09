namespace Microsoft.OneFuzz.Service;

public record BaseResponse();

public record CanSchedule(
    bool Allowed,
    bool WorkStopped
) : BaseResponse;

public record PendingNodeCommand(
    NodeCommandEvenlope? Enveleope
) : BaseResponse;

public record BoolResult(
    bool Result
) : BaseResponse;
