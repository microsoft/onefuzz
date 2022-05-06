namespace Microsoft.OneFuzz.Service;

public record BaseResponse();

public record CanSchedule(
    bool Allowed,
    bool WorkStopped
) : BaseResponse;
