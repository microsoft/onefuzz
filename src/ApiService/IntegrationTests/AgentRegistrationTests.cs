using System;
using System.Linq;
using System.Net;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Functions;
using Xunit;
using Xunit.Abstractions;

using Async = System.Threading.Tasks;
using Node = Microsoft.OneFuzz.Service.Node;
using Pool = Microsoft.OneFuzz.Service.Pool;

namespace IntegrationTests;

[Trait("Category", "Live")]
public class AzureStorageAgentRegistrationTest : AgentRegistrationTestsBase {
    public AzureStorageAgentRegistrationTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteAgentRegistrationTest : AgentRegistrationTestsBase {
    public AzuriteAgentRegistrationTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class AgentRegistrationTestsBase : FunctionTestBase {
    public AgentRegistrationTestsBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) { }

    private readonly Guid _machineId = Guid.NewGuid();
    private readonly Guid _poolId = Guid.NewGuid();
    private readonly Guid _scalesetId = Guid.NewGuid();
    private readonly PoolName _poolName = PoolName.Parse($"pool-{Guid.NewGuid()}");

    [Fact]
    public async Async.Task Authorization_IsRequired() {
        var auth = new TestEndpointAuthorization(RequestType.NoAuthorization, Logger, Context);
        var func = new AgentRegistration(Logger, auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty("POST"));
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Async.Task UserAuthorization_IsNotPermitted() {
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new AgentRegistration(Logger, auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty("POST"));
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Async.Task AgentAuthorization_IsAccepted() {
        var auth = new TestEndpointAuthorization(RequestType.Agent, Logger, Context);
        var func = new AgentRegistration(Logger, auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty("POST"));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode); // BadRequest due to missing parameters, not Unauthorized
    }

    [Fact]
    public async Async.Task Get_UrlParameterRequired() {
        var auth = new TestEndpointAuthorization(RequestType.Agent, Logger, Context);
        var func = new AgentRegistration(Logger, auth, Context);

        var req = TestHttpRequestData.Empty("GET");
        var result = await func.Run(req);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var body = BodyAs<Error>(result);
        Assert.Equal(ErrorCode.INVALID_REQUEST, body.Code);
        Assert.Equal("'machine_id' query parameter must be provided", body.Errors?.Single());
    }

    [Fact]
    public async Async.Task Get_MissingNode() {
        var auth = new TestEndpointAuthorization(RequestType.Agent, Logger, Context);
        var func = new AgentRegistration(Logger, auth, Context);

        var req = TestHttpRequestData.Empty("GET");
        req.SetUrlParameter("machine_id", _machineId);

        var result = await func.Run(req);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var body = BodyAs<Error>(result);
        Assert.Equal(ErrorCode.INVALID_REQUEST, body.Code);
        Assert.Contains("unable to find a registration", body.Errors?.Single());
    }

    [Fact]
    public async Async.Task Get_MissingPool() {
        await Context.InsertAll(
            new Node(_poolName, _machineId, _poolId, "1.0.0"));

        var auth = new TestEndpointAuthorization(RequestType.Agent, Logger, Context);
        var func = new AgentRegistration(Logger, auth, Context);

        var req = TestHttpRequestData.Empty("GET");
        req.SetUrlParameter("machine_id", _machineId);

        var result = await func.Run(req);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var body = BodyAs<Error>(result);
        Assert.Equal(ErrorCode.INVALID_REQUEST, body.Code);
        Assert.Contains("unable to find a pool", body.Errors?.Single());
    }

    [Fact]
    public async Async.Task Get_HappyPath() {
        await Context.InsertAll(
            new Node(_poolName, _machineId, _poolId, "1.0.0"),
            new Pool(_poolName, _poolId, Os.Linux, false, Architecture.x86_64, PoolState.Init, null));

        var auth = new TestEndpointAuthorization(RequestType.Agent, Logger, Context);
        var func = new AgentRegistration(Logger, auth, Context);

        var req = TestHttpRequestData.Empty("GET");
        req.SetUrlParameter("machine_id", _machineId);

        var result = await func.Run(req);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var body = BodyAs<AgentRegistrationResponse>(result);
        Assert.NotNull(body.CommandsUrl);
        Assert.NotNull(body.EventsUrl);
        Assert.NotNull(body.WorkQueue);
    }

    [Fact]
    public async Async.Task Post_SetsDefaultVersion_IfNotSupplied() {
        await Context.InsertAll(
            new Pool(_poolName, _poolId, Os.Linux, false, Architecture.x86_64, PoolState.Init, null));

        var auth = new TestEndpointAuthorization(RequestType.Agent, Logger, Context);
        var func = new AgentRegistration(Logger, auth, Context);

        var req = TestHttpRequestData.Empty("POST");
        req.SetUrlParameter("machine_id", _machineId);
        req.SetUrlParameter("pool_name", _poolName);
        req.SetUrlParameter("scaleset_id", _scalesetId);

        var result = await func.Run(req);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // should be one node with correct version
        var nodes = await Context.NodeOperations.SearchAll().ToListAsync();
        var node = Assert.Single(nodes);
        Assert.Equal("1.0.0", node.Version);
    }

    [Fact]
    public async Async.Task Post_SetsCorrectVersion() {
        await Context.InsertAll(
            new Pool(_poolName, _poolId, Os.Linux, false, Architecture.x86_64, PoolState.Init, null));

        var auth = new TestEndpointAuthorization(RequestType.Agent, Logger, Context);
        var func = new AgentRegistration(Logger, auth, Context);

        var req = TestHttpRequestData.Empty("POST");
        req.SetUrlParameter("machine_id", _machineId);
        req.SetUrlParameter("pool_name", _poolName);
        req.SetUrlParameter("scaleset_id", _scalesetId);
        req.SetUrlParameter("version", "1.2.3");

        var result = await func.Run(req);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // should be one node with provided version
        var nodes = await Context.NodeOperations.SearchAll().ToListAsync();
        var node = Assert.Single(nodes);
        Assert.Equal("1.2.3", node.Version);
    }

    [Fact]
    public async Async.Task Post_DeletesExistingNodeForMachineId() {
        await Context.InsertAll(
            new Node(PoolName.Parse("another-pool"), _machineId, _poolId, "1.0.0"),
            new Pool(_poolName, _poolId, Os.Linux, false, Architecture.x86_64, PoolState.Init, null));

        var auth = new TestEndpointAuthorization(RequestType.Agent, Logger, Context);
        var func = new AgentRegistration(Logger, auth, Context);

        var req = TestHttpRequestData.Empty("POST");
        req.SetUrlParameter("machine_id", _machineId);
        req.SetUrlParameter("pool_name", _poolName);
        req.SetUrlParameter("scaleset_id", _scalesetId);

        var result = await func.Run(req);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // there should only be one node, the old one with the same machineID was deleted
        var nodes = await Context.NodeOperations.SearchAll().ToListAsync();
        var node = Assert.Single(nodes);
        Assert.Equal(_poolName, node.PoolName);
    }

    [Theory]
    [InlineData("machine_id")]
    [InlineData("pool_name")]
    public async Async.Task Post_ChecksRequiredParameters(string parameterToSkip) {
        await Context.InsertAll(
            new Pool(_poolName, _poolId, Os.Linux, false, Architecture.x86_64, PoolState.Init, null));

        var auth = new TestEndpointAuthorization(RequestType.Agent, Logger, Context);
        var func = new AgentRegistration(Logger, auth, Context);

        var req = TestHttpRequestData.Empty("POST");
        if (parameterToSkip != "machine_id") {
            req.SetUrlParameter("machine_id", _machineId);
        }

        if (parameterToSkip != "pool_name") {
            req.SetUrlParameter("pool_name", _poolName);
        }

        var result = await func.Run(req);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);

        var err = BodyAs<Error>(result);
        Assert.Equal(ErrorCode.INVALID_REQUEST, err.Code);
        Assert.Equal($"'{parameterToSkip}' query parameter must be provided", err.Errors?.Single());
    }
}
