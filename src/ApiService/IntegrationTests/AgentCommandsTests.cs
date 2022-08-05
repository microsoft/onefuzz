using System.Net;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Functions;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;

namespace IntegrationTests;

[Trait("Category", "Live")]
public class AzureStorageAgentCommandsTest : AgentCommandsTestsBase {
    public AzureStorageAgentCommandsTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteAgentCommandsTest : AgentEventsTestsBase {
    public AzuriteAgentCommandsTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class AgentCommandsTestsBase : FunctionTestBase {
    public AgentCommandsTestsBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) { }


    [Fact]
    public async Async.Task Authorization_IsRequired() {
        var auth = new TestEndpointAuthorization(RequestType.NoAuthorization, Logger, Context);
        var func = new AgentCommands(Logger, auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty("GET"));
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Async.Task UserAuthorization_IsNotPermitted() {
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new AgentCommands(Logger, auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty("GET"));
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Async.Task AgentAuthorization_IsAccepted() {
        var auth = new TestEndpointAuthorization(RequestType.Agent, Logger, Context);
        var func = new AgentCommands(Logger, auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty("GET"));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode); // BadRequest due to no body, not Unauthorized
    }
}
