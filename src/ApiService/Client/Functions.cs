using System.Net.Http;
using Microsoft.OneFuzz.Api;

namespace Microsoft.OneFuzz.Client;

/// Defines a specific HTTP function with the specified request & response types:
internal readonly record struct HttpFunction<TReq, TResp>(string Path, HttpMethod Method);

// A marker that the request or response is empty:
internal readonly struct None { };

// Defines the available functions on the service:
internal static class Functions {
    // internal static readonly HttpFunction<None, List<ContainerInfo>> ContainersList = new("containers", HttpMethod.Get);

    internal static readonly HttpFunction<None, InfoResponse> Info = new("info", HttpMethod.Get);
}
