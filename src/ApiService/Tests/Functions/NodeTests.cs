
using System;
using System.Linq;
using System.Net;
using Microsoft.OneFuzz.Service;
using Tests.Fakes;
using Xunit;
using Xunit.Abstractions;

using Async = System.Threading.Tasks;

namespace Tests.Functions;

[Trait("Category", "Integration")]
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
        : base(output, storage) { }

    private readonly Guid _machineId = Guid.NewGuid();
    private readonly PoolName _poolName = PoolName.Parse($"pool-{Guid.NewGuid()}");
    private readonly string _version = Guid.NewGuid().ToString();

    [Fact]
    public async Async.Task Search_SpecificNode_NotFound_ReturnsNotFound() {
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        var req = new NodeSearch(MachineId: _machineId);
        var func = new NodeFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Async.Task Search_SpecificNode_Found_ReturnsOk() {
        await Context.InsertAll(
            new Node(_poolName, _machineId, null, _version));

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        var req = new NodeSearch(MachineId: _machineId);
        var func = new NodeFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // make sure we got the data from the table
        var deserialized = BodyAs<NodeSearchResult>(result);
        Assert.Equal(_version, deserialized.Version);
    }

    [Theory]
    [InlineData("PATCH")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Async.Task RequiresAdmin(string method) {
        // config must be found
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!));

        // must be a user to auth
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        // override the found user credentials
        var userInfo = new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: Guid.NewGuid(), "upn");
        Context.UserCredentials = new TestUserCredentials(Logger, Context.ConfigOperations, OneFuzzResult<UserInfo>.Ok(userInfo));

        var req = new NodeGet(MachineId: _machineId);
        var func = new NodeFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.FromJson(method, req));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);

        var err = BodyAs<Error>(result);
        Assert.Equal(ErrorCode.UNAUTHORIZED, err.Code);
        Assert.Contains("pool modification disabled", err.Errors?.Single());
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

        // must be a user to auth
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        // override the found user credentials
        var userInfo = new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: Guid.NewGuid(), "upn");
        Context.UserCredentials = new TestUserCredentials(Logger, Context.ConfigOperations, OneFuzzResult<UserInfo>.Ok(userInfo));

        var req = new NodeGet(MachineId: _machineId);
        var func = new NodeFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.FromJson(method, req));

        // we will fail with BadRequest but due to not being able to find the Node,
        // not because of UNAUTHORIZED
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(ErrorCode.UNABLE_TO_FIND, BodyAs<Error>(result).Code);
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

        // must be a user to auth
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        // override the found user credentials
        var userInfo = new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: userObjectId, "upn");
        Context.UserCredentials = new TestUserCredentials(Logger, Context.ConfigOperations, OneFuzzResult<UserInfo>.Ok(userInfo));

        var req = new NodeGet(MachineId: _machineId);
        var func = new NodeFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.FromJson(method, req));

        // we will fail with BadRequest but due to not being able to find the Node,
        // not because of UNAUTHORIZED
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(ErrorCode.UNABLE_TO_FIND, BodyAs<Error>(result).Code);
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
                Admins = new[] { otherObjectId }
            });

        // must be a user to auth
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        // override the found user credentials
        var userInfo = new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: userObjectId, "upn");
        Context.UserCredentials = new TestUserCredentials(Logger, Context.ConfigOperations, OneFuzzResult<UserInfo>.Ok(userInfo));

        var req = new NodeGet(MachineId: _machineId);
        var func = new NodeFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.FromJson(method, req));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);

        var err = BodyAs<Error>(result);
        Assert.Equal(ErrorCode.UNAUTHORIZED, err.Code);
        Assert.Contains("not authorized to manage pools", err.Errors?.Single());
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

        // must be a user to auth
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        // override the found user credentials
        var userInfo = new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: Guid.NewGuid(), "upn");
        Context.UserCredentials = new TestUserCredentials(Logger, Context.ConfigOperations, OneFuzzResult<UserInfo>.Ok(userInfo));

        // all of these operations use NodeGet
        var req = new NodeGet(MachineId: _machineId);
        var func = new NodeFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.FromJson(method, req));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }
}
