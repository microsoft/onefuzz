using System;
using System.Collections.Generic;

namespace Microsoft.OneFuzz.Api;

public record InfoResponse(
    string ResourceGroup,
    string Region,
    string Subscription,
    IReadOnlyDictionary<string, InfoVersion> Versions,
    Guid? InstanceId,
    string? InsightsAppid,
    string? InsightsInstrumentationKey
);

public record InfoVersion(
    string Git,
    string Build,
    string Version);
