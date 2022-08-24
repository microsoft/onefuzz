using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.OneFuzz.Api;

namespace Microsoft.OneFuzz.Client;

// A marker that input or output is empty.
internal readonly struct None { };

internal sealed record HttpFunction<TReq, TResp>(Uri Path, HttpMethod Method);

internal static class Functions {
    internal static class Containers {
        static readonly Uri _path = new("containers", UriKind.Relative);
        // internal static readonly HttpFunction<None, List<ContainerInfo>> List = new(_path, HttpMethod.Get);
    }

    internal static readonly HttpFunction<None, InfoResponse> Info = new(new Uri("info", UriKind.Relative), HttpMethod.Get);
}
