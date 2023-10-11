using Xunit;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service;
using Microsoft.Extensions.Logging.Abstractions;

namespace FunctionalTests;

[Trait("Category", "Live")]
public class TestVersionCheckMiddleware {
    private sealed class MockServiceConfiguration : ServiceConfiguration {
        public new string OneFuzzVersion { get; } = "1.0.0";
    }
    private static Program.VersionCheckingMiddleware GetMiddleware() {
        return new Program.VersionCheckingMiddleware(
            new MockServiceConfiguration(),
            new RequestHandling(NullLogger.Instance)
        );
    }

    private static HttpHeadersCollection GetHeaders(string? cliVersion = null, string? strictVersionCheck = null) {
        var headers = new HttpHeadersCollection();
        if (cliVersion != null) {
            headers.Add("cli-version", cliVersion);
        }
        if (strictVersionCheck != null) {
            headers.Add("strict-version", strictVersionCheck);
        }
        return headers;
    }

    [Fact]
    public void VersionCheck_NoHeaders_ReturnsNull() {
        var middleware = GetMiddleware();
        var headers = GetHeaders();

        var result = middleware.TestCliVersion(headers);

        Assert.Null(result);
    }
}
