namespace Microsoft.OneFuzz.Service;

public record BaseRequest();

public record CanScheduleRequest(
    Guid MachineId,
    Guid TaskId
) : BaseRequest;

public record NodeCommandGet(
    Guid MachineId
) : BaseRequest;

public record NodeCommandDelete(
    Guid MachineId,
    string MessageId
) : BaseRequest;
