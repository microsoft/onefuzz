using Azure.Core;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OneFuzz.Service;
using Semver;
using Xunit;

namespace FunctionalTests;

[Trait("Category", "Live")]
public class TestVersionCheckMiddleware {
    private sealed class MockServiceConfiguration : IServiceConfig {
        public LogDestination[] LogDestinations => throw new NotImplementedException();

        public SeverityLevel LogSeverityLevel => throw new NotImplementedException();

        public string? ApplicationInsightsAppId => throw new NotImplementedException();

        public string? ApplicationInsightsInstrumentationKey => throw new NotImplementedException();

        public string? AppConfigurationEndpoint => throw new NotImplementedException();

        public string? AppConfigurationConnectionString => throw new NotImplementedException();

        public string? CliAppId => throw new NotImplementedException();

        public string? Authority => throw new NotImplementedException();

        public string? TenantDomain => throw new NotImplementedException();

        public string? MultiTenantDomain => throw new NotImplementedException();

        public ResourceIdentifier OneFuzzResourceGroup => throw new NotImplementedException();

        public ResourceIdentifier OneFuzzDataStorage => throw new NotImplementedException();

        public ResourceIdentifier OneFuzzFuncStorage => throw new NotImplementedException();

        public Uri OneFuzzInstance => throw new NotImplementedException();

        public string OneFuzzInstanceName => throw new NotImplementedException();

        public Uri? OneFuzzEndpoint => throw new NotImplementedException();

        public string OneFuzzKeyvault => throw new NotImplementedException();

        public string? OneFuzzMonitor => throw new NotImplementedException();

        public string? OneFuzzOwner => throw new NotImplementedException();

        public string? OneFuzzTelemetry => throw new NotImplementedException();

        public string OneFuzzVersion { get; } = "1.0.0";

        public string? OneFuzzAllowOutdatedAgent => throw new NotImplementedException();

        public string OneFuzzStoragePrefix => throw new NotImplementedException();
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
    public void VersionCheck_NoHeaders_ReturnsOk() {
        var middleware = GetMiddleware();
        var headers = GetHeaders();

        var result = middleware.CheckCliVersion(headers);

        Assert.True(result.IsOk);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.0.1")]
    [InlineData("BadVersionFormat")]
    public void VersionCheck_JustCliVersion_ReturnsOk(string cliVersion) {
        var middleware = GetMiddleware();
        var headers = GetHeaders(cliVersion);

        var result = middleware.CheckCliVersion(headers);

        Assert.True(result.IsOk);
    }

    [Fact]
    public void VersionCheck_JustStrictVersionTrue_ReturnsInvalidRequest() {
        var middleware = GetMiddleware();
        var headers = GetHeaders(null, "True");

        var result = middleware.CheckCliVersion(headers);

        Assert.False(result.IsOk);
        Assert.Equal(ErrorCode.INVALID_REQUEST, result.ErrorV.Code);
        Assert.Contains("is set to true without a corresponding", result.ErrorV.Errors?.First());
    }

    [Theory]
    [InlineData("False")]
    [InlineData("Something else")]
    public void VersionCheck_JustStrictVersionNotTrue_ReturnsOk(string strictVersion) {
        var middleware = GetMiddleware();
        var headers = GetHeaders(null, strictVersion);

        var result = middleware.CheckCliVersion(headers);

        Assert.True(result.IsOk);
    }

    [Theory]
    [InlineData("1.0.0", "False")]
    [InlineData("0.0.1", "Something else")]
    [InlineData("BadVersionFormat", "Not true")]
    public void VersionCheck_StrictVersionNotTrue_ReturnsOk(string cliVersion, string strictVersion) {
        var middleware = GetMiddleware();
        var headers = GetHeaders(cliVersion, strictVersion);

        var result = middleware.CheckCliVersion(headers);

        Assert.True(result.IsOk);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.0.0+90b616cc9c742ee3dd085802e713a6fd0054e624")]
    [InlineData("1.1.0+meta")]
    public void VersionCheck_ValidVersion_ReturnsOk(string cliVersion) {
        var middleware = GetMiddleware();
        var headers = GetHeaders(cliVersion, "True");

        var result = middleware.CheckCliVersion(headers);

        Assert.True(result.IsOk, result.ErrorV?.Errors?.FirstOrDefault());
    }

    [Theory]
    [InlineData("0.9.1", ErrorCode.INVALID_CLI_VERSION, "cli is out of date")]
    [InlineData("1.0.0-pre.release", ErrorCode.INVALID_CLI_VERSION, "cli is out of date")]
    [InlineData("Bad Format", ErrorCode.INVALID_CLI_VERSION, "not a valid sematic version")]
    public void VersionCheck_InvalidVersion_ReturnsInvalidRequest(string cliVersion, ErrorCode expectedCode, string expectedMessage) {
        var middleware = GetMiddleware();
        var headers = GetHeaders(cliVersion, "True");

        var result = middleware.CheckCliVersion(headers);

        Assert.False(result.IsOk);
        Assert.Equal(expectedCode, result.ErrorV.Code);
        Assert.Contains(expectedMessage, result.ErrorV.Errors?.First());
    }
}
