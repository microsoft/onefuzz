
using System;
using System.Collections.Generic;
using System.Net;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Auth;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;
using NodeFunction = Microsoft.OneFuzz.Service.Functions.Node;

namespace IntegrationTests.Functions;

[Trait("Category", "Live")]
public class AzureStorageNodeTest : NodeTestBase {
    public AzureStorageNodeTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteNodeTest : NodeTestBase {
    public AzuriteNodeTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class NodeTestBase : FunctionTestBase {
    public NodeTestBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) {
        _scalesetId = Scaleset.GenerateNewScalesetId(_poolName);
    }

    private readonly Guid _machineId = Guid.NewGuid();
    private readonly ScalesetId _scalesetId;
    private readonly PoolName _poolName = PoolName.Parse($"pool-{Guid.NewGuid()}");
    private readonly string _version = Guid.NewGuid().ToString();

    [Fact]
    public async Async.Task Search_SpecificNode_NotFound_ReturnsNotFound() {
        var req = new NodeSearch(MachineId: _machineId);
        var func = new NodeFunction(Logger, Context.EndpointAuthorization, Context);
        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req), ctx);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Async.Task Search_SpecificNode_Found_ReturnsOk() {
        await Context.InsertAll(
            new Node(_poolName, _machineId, null, _version));

        var req = new NodeSearch(MachineId: _machineId);
        var func = new NodeFunction(Logger, Context.EndpointAuthorization, Context);
        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req), ctx);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // make sure we got the data from the table
        var deserialized = BodyAs<NodeSearchResult>(result);
        Assert.Equal(_version, deserialized.Version);
    }

    [Fact]
    public async Async.Task Search_MultipleNodes_CanFindNone() {
        var req = new NodeSearch();
        var func = new NodeFunction(Logger, Context.EndpointAuthorization, Context);
        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req), ctx);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("[]", BodyAsString(result));
    }

    [Fact]
    public async Async.Task Search_MultipleNodes_ByPoolName() {
        await Context.InsertAll(
            new Node(PoolName.Parse("otherPool"), Guid.NewGuid(), null, _version),
            new Node(_poolName, Guid.NewGuid(), null, _version),
            new Node(_poolName, Guid.NewGuid(), null, _version));

        var req = new NodeSearch(PoolName: _poolName);

        var func = new NodeFunction(Logger, Context.EndpointAuthorization, Context);
        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req), ctx);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // make sure we got the data from the table
        var deserialized = BodyAs<NodeSearchResult[]>(result);
        Assert.Equal(2, deserialized.Length);
    }

    [Fact]
    public async Async.Task Search_MultipleNodes_ByScalesetId() {
        await Context.InsertAll(
            new Node(_poolName, Guid.NewGuid(), null, _version, ScalesetId: _scalesetId),
            new Node(_poolName, Guid.NewGuid(), null, _version, ScalesetId: _scalesetId),
            new Node(_poolName, Guid.NewGuid(), null, _version));

        var req = new NodeSearch(ScalesetId: _scalesetId);

        var func = new NodeFunction(Logger, Context.EndpointAuthorization, Context);
        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req), ctx);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // make sure we got the data from the table
        var deserialized = BodyAs<NodeSearchResult[]>(result);
        Assert.Equal(2, deserialized.Length);
    }


    [Fact]
    public async Async.Task Search_MultipleNodes_ByState() {
        await Context.InsertAll(
            new Node(_poolName, Guid.NewGuid(), null, _version, State: NodeState.Busy),
            new Node(_poolName, Guid.NewGuid(), null, _version, State: NodeState.Busy),
            new Node(_poolName, Guid.NewGuid(), null, _version));

        var req = new NodeSearch(State: new List<NodeState> { NodeState.Busy });

        var func = new NodeFunction(Logger, Context.EndpointAuthorization, Context);
        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req), ctx);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // make sure we got the data from the table
        var deserialized = BodyAs<NodeSearchResult[]>(result);
        Assert.Equal(2, deserialized.Length);
    }

    [Fact]
    public async Async.Task Search_MultipleNodes_ByMultipleStates() {
        await Context.InsertAll(
            new Node(_poolName, Guid.NewGuid(), null, _version, State: NodeState.Free),
            new Node(_poolName, Guid.NewGuid(), null, _version, State: NodeState.Busy),
            new Node(_poolName, Guid.NewGuid(), null, _version, State: NodeState.Busy),
            new Node(_poolName, Guid.NewGuid(), null, _version));

        var req = new NodeSearch(State: new List<NodeState> { NodeState.Free, NodeState.Busy });

        var func = new NodeFunction(Logger, Context.EndpointAuthorization, Context);
        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req), ctx);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // make sure we got the data from the table
        var deserialized = BodyAs<NodeSearchResult[]>(result);
        Assert.Equal(3, deserialized.Length);
    }

    [Theory]
    [InlineData("PATCH")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Async.Task RequiresAdmin(string method) {
        // config must be found
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!) {
                RequireAdminPrivileges = true
            });

        // override the found user credentials
        var ctx = new TestFunctionContext();
        ctx.SetUserAuthInfo(new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: Guid.NewGuid(), "upn"));

        var req = new NodeGet(MachineId: _machineId);
        var func = new NodeFunction(Logger, Context.EndpointAuthorization, Context);
        var result = await func.Run(TestHttpRequestData.FromJson(method, req), ctx);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);

        var err = BodyAs<ProblemDetails>(result);
        Assert.Equal(ErrorCode.UNAUTHORIZED.ToString(), err.Title);
        Assert.Contains("pool modification disabled", err.Detail);
    }

    [Theory]
    [InlineData("PATCH")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Async.Task RequiresAdmin_CanBeDisabled(string method) {
        // disable requiring admin privileges
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!) {
                RequireAdminPrivileges = false
            });

        // override the found user credentials
        var ctx = new TestFunctionContext();
        ctx.SetUserAuthInfo(new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: Guid.NewGuid(), "upn"));

        var req = new NodeGet(MachineId: _machineId);
        var func = new NodeFunction(Logger, Context.EndpointAuthorization, Context);
        var result = await func.Run(TestHttpRequestData.FromJson(method, req), ctx);

        // we will fail with BadRequest but due to not being able to find the Node,
        // not because of UNAUTHORIZED
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(ErrorCode.UNABLE_TO_FIND.ToString(), BodyAs<ProblemDetails>(result).Title);
    }

    [Theory]
    [InlineData("PATCH")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Async.Task UserCanBeAdmin(string method) {
        var userObjectId = Guid.NewGuid();

        // config specifies that user is admin
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!) {
                Admins = new[] { userObjectId }
            });

        // override the found user credentials
        var ctx = new TestFunctionContext();
        ctx.SetUserAuthInfo(new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: userObjectId, "upn"));

        var req = new NodeGet(MachineId: _machineId);
        var func = new NodeFunction(Logger, Context.EndpointAuthorization, Context);
        var result = await func.Run(TestHttpRequestData.FromJson(method, req), ctx);

        // we will fail with BadRequest but due to not being able to find the Node,
        // not because of UNAUTHORIZED
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(ErrorCode.UNABLE_TO_FIND.ToString(), BodyAs<ProblemDetails>(result).Title);
    }

    [Theory]
    [InlineData("PATCH")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Async.Task EnablingAdminForAnotherUserDoesNotPermitThisUser(string method) {
        var userObjectId = Guid.NewGuid();
        var otherObjectId = Guid.NewGuid();

        // config specifies that a different user is admin
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!) {
                Admins = new[] { otherObjectId }, RequireAdminPrivileges = true
            });

        // override the found user credentials
        var ctx = new TestFunctionContext();
        ctx.SetUserAuthInfo(new UserAuthInfo(
            new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: userObjectId, "upn"),
            new List<string>()));

        var req = new NodeGet(MachineId: _machineId);
        var func = new NodeFunction(Logger, Context.EndpointAuthorization, Context);
        var result = await func.Run(TestHttpRequestData.FromJson(method, req), ctx);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);

        var err = BodyAs<ProblemDetails>(result);
        Assert.Equal(ErrorCode.UNAUTHORIZED.ToString(), err.Title);
        Assert.Contains("not authorized to manage instance", err.Detail);
    }

    [Theory]
    [InlineData("PATCH")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Async.Task CanPerformOperation(string method) {
        // disable requiring admin privileges
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!) {
                RequireAdminPrivileges = false
            },
            new Node(_poolName, _machineId, null, _version));

        // override the found user credentials
        var ctx = new TestFunctionContext();
        ctx.SetUserAuthInfo(new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: Guid.NewGuid(), "upn"));

        // all of these operations use NodeGet
        var req = new NodeGet(MachineId: _machineId);
        var func = new NodeFunction(Logger, Context.EndpointAuthorization, Context);
        var result = await func.Run(TestHttpRequestData.FromJson(method, req), ctx);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }
}
