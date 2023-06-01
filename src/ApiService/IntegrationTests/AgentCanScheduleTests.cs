using System.Net;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Functions;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;

namespace IntegrationTests;

[Trait("Category", "Live")]
public class AzureStorageAgentCanScheduleTest : AgentCommandsTestsBase {
    public AzureStorageAgentCanScheduleTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteAgentCanScheduleTest : AgentEventsTestsBase {
    public AzuriteAgentCanScheduleTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class AgentCanScheduleTestsBase : FunctionTestBase {
    public AgentCanScheduleTestsBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) { }


    [Fact]
    public async Async.Task Authorization_IsRequired() {
        var auth = new TestEndpointAuthorization(RequestType.NoAuthorization, LoggerProvider.CreateLogger<EndpointAuthorization>(), Context);
        var func = new AgentCanSchedule(LoggerProvider.CreateLogger<AgentCanSchedule>(), auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty("POST"));
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Async.Task UserAuthorization_IsNotPermitted() {
        var auth = new TestEndpointAuthorization(RequestType.User, LoggerProvider.CreateLogger<EndpointAuthorization>(), Context);
        var func = new AgentCanSchedule(LoggerProvider.CreateLogger<AgentCanSchedule>(), auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty("POST"));
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Async.Task AgentAuthorization_IsAccepted() {
        var auth = new TestEndpointAuthorization(RequestType.Agent, LoggerProvider.CreateLogger<EndpointAuthorization>(), Context);
        var func = new AgentCanSchedule(LoggerProvider.CreateLogger<AgentCanSchedule>(), auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty("POST"));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode); // BadRequest due to no body, not Unauthorized
    }
}
