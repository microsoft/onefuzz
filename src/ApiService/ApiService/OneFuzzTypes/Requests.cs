namespace Microsoft.OneFuzz.Service;

public record BaseRequest();

public record CanScheduleRequest(
    Guid MachineId,
    Guid TaskId
) : BaseRequest;
