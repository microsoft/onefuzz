

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;
using PoolFunction = Microsoft.OneFuzz.Service.Functions.Pool;

namespace IntegrationTests.Functions;

[Trait("Category", "Live")]
public class AzureStoragePoolTest : PoolTestBase {
    public AzureStoragePoolTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuritePoolTest : PoolTestBase {
    public AzuritePoolTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class PoolTestBase : FunctionTestBase {
    public PoolTestBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) { }

    private readonly Guid _userObjectId = Guid.NewGuid();
    private readonly Guid _poolId = Guid.NewGuid();
    private readonly PoolName _poolName = PoolName.Parse("pool-" + Guid.NewGuid());


    [Theory]
    [InlineData("POST", RequestType.Agent)]
    [InlineData("POST", RequestType.NoAuthorization)]
    [InlineData("GET", RequestType.Agent)]
    [InlineData("GET", RequestType.NoAuthorization)]
    [InlineData("DELETE", RequestType.Agent)]
    [InlineData("DELETE", RequestType.NoAuthorization)]
    public async Async.Task UserAuthorization_IsRequired(string method, RequestType authType) {
        var auth = new TestEndpointAuthorization(authType, Logger, Context);
        var func = new PoolFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.Empty(method));
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Async.Task Search_SpecificPool_ById_NotFound_ReturnsBadRequest() {
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        var req = new PoolSearch(PoolId: _poolId);
        var func = new PoolFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Async.Task Search_SpecificPool_ById_CanFind() {
        await Context.InsertAll(
            new Pool(_poolName, _poolId, Os.Linux, true, Architecture.x86_64, PoolState.Running, null));

        // queue must exist
        await Context.Queue.CreateQueue(Context.PoolOperations.GetPoolQueue(_poolId), StorageType.Corpus);

        // use test class to override instance ID
        Context.Containers = new TestContainers(Logger, Context.Storage, Context.Creds, Context.ServiceConfiguration);

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        var req = new PoolSearch(PoolId: _poolId);
        var func = new PoolFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var pool = BodyAs<PoolGetResult>(result);
        Assert.Equal(_poolId, pool.PoolId);
    }

    [Fact]
    public async Async.Task Search_SpecificPool_ByName_NotFound_ReturnsBadRequest() {
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        var req = new PoolSearch(Name: _poolName);
        var func = new PoolFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Async.Task Search_SpecificPool_ByName_CanFind() {
        await Context.InsertAll(
            new Pool(_poolName, _poolId, Os.Linux, true, Architecture.x86_64, PoolState.Running, null));

        // queue must exist
        await Context.Queue.CreateQueue(Context.PoolOperations.GetPoolQueue(_poolId), StorageType.Corpus);

        // use test class to override instance ID
        Context.Containers = new TestContainers(Logger, Context.Storage, Context.Creds, Context.ServiceConfiguration);

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        var req = new PoolSearch(Name: _poolName);
        var func = new PoolFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var pool = BodyAs<PoolGetResult>(result);
        Assert.Equal(_poolName, pool.Name);
    }

    [Fact]
    public async Async.Task Search_SpecificPool_ByState_NotFound_ReturnsEmptyResult() {
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        var req = new PoolSearch(State: new List<PoolState> { PoolState.Init });
        var func = new PoolFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        Assert.Equal("[]", BodyAsString(result));
    }

    [Fact]
    public async Async.Task Search_SpecificPool_NoQuery_ReturnsAllPools() {
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!) { Admins = new[] { _userObjectId } }, // needed for admin check
            new Pool(_poolName, _poolId, Os.Linux, true, Architecture.x86_64, PoolState.Running, null));

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        var func = new PoolFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.FromJson("GET", new PoolSearch()));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var pool = BodyAs<PoolGetResult[]>(result);
        Assert.Equal(_poolName, pool.Single().Name);
    }


    [Fact]
    public async Async.Task Delete_NotNow_PoolEntersShutdownState() {
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!) { Admins = new[] { _userObjectId } }, // needed for admin check
            new Pool(_poolName, _poolId, Os.Linux, true, Architecture.x86_64, PoolState.Running, null));

        // override the found user credentials - need these to check for admin
        var userInfo = new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: _userObjectId, "upn");
        Context.UserCredentials = new TestUserCredentials(Logger, Context.ConfigOperations, OneFuzzResult<UserInfo>.Ok(userInfo));

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new PoolFunction(Logger, auth, Context);

        var req = new PoolStop(Name: _poolName, Now: false);
        var result = await func.Run(TestHttpRequestData.FromJson("DELETE", req));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var pool = await Context.PoolOperations.GetByName(_poolName);
        Assert.True(pool.IsOk);
        Assert.Equal(PoolState.Shutdown, pool.OkV!.State);
    }

    [Fact]
    public async Async.Task Delete_NotNow_PoolStaysInHaltedState_IfAlreadyHalted() {
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!) { Admins = new[] { _userObjectId } }, // needed for admin check
            new Pool(_poolName, _poolId, Os.Linux, true, Architecture.x86_64, PoolState.Halt, null));

        // override the found user credentials - need these to check for admin
        var userInfo = new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: _userObjectId, "upn");
        Context.UserCredentials = new TestUserCredentials(Logger, Context.ConfigOperations, OneFuzzResult<UserInfo>.Ok(userInfo));

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new PoolFunction(Logger, auth, Context);

        var req = new PoolStop(Name: _poolName, Now: false);
        var result = await func.Run(TestHttpRequestData.FromJson("DELETE", req));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var pool = await Context.PoolOperations.GetByName(_poolName);
        Assert.True(pool.IsOk);
        Assert.Equal(PoolState.Halt, pool.OkV!.State);
    }

    [Fact]
    public async Async.Task Delete_Now_PoolEntersHaltState() {
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!) { Admins = new[] { _userObjectId } }, // needed for admin check
            new Pool(_poolName, _poolId, Os.Linux, true, Architecture.x86_64, PoolState.Running, null));

        // override the found user credentials - need these to check for admin
        var userInfo = new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: _userObjectId, "upn");
        Context.UserCredentials = new TestUserCredentials(Logger, Context.ConfigOperations, OneFuzzResult<UserInfo>.Ok(userInfo));

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new PoolFunction(Logger, auth, Context);

        var req = new PoolStop(Name: _poolName, Now: true);
        var result = await func.Run(TestHttpRequestData.FromJson("DELETE", req));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var pool = await Context.PoolOperations.GetByName(_poolName);
        Assert.True(pool.IsOk);
        Assert.Equal(PoolState.Halt, pool.OkV!.State);
    }

    [Fact]
    public async Async.Task Post_CreatesNewPool() {
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!) { Admins = new[] { _userObjectId } }); // needed for admin check

        // override the found user credentials - need these to check for admin
        var userInfo = new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: _userObjectId, "upn");
        Context.UserCredentials = new TestUserCredentials(Logger, Context.ConfigOperations, OneFuzzResult<UserInfo>.Ok(userInfo));

        // need to override instance id
        Context.Containers = new TestContainers(Logger, Context.Storage, Context.Creds, Context.ServiceConfiguration);

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new PoolFunction(Logger, auth, Context);

        var req = new PoolCreate(Name: _poolName, Os.Linux, Architecture.x86_64, true);
        var result = await func.Run(TestHttpRequestData.FromJson("POST", req));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // should get a pool back
        var returnedPool = BodyAs<PoolGetResult>(result);
        Assert.Equal(_poolName, returnedPool.Name);
        var poolId = returnedPool.PoolId;

        // should exist in storage
        var pool = await Context.PoolOperations.GetByName(_poolName);
        Assert.True(pool.IsOk);
        Assert.Equal(poolId, pool.OkV!.PoolId);
    }

    [Fact]
    public async Async.Task Post_DoesNotCreatePool_IfOneWithTheSameNameAlreadyExists() {
        await Context.InsertAll(
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!) { Admins = new[] { _userObjectId } }, // needed for admin check
            new Pool(_poolName, _poolId, Os.Linux, true, Architecture.x86_64, PoolState.Running, null));

        // override the found user credentials - need these to check for admin
        var userInfo = new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: _userObjectId, "upn");
        Context.UserCredentials = new TestUserCredentials(Logger, Context.ConfigOperations, OneFuzzResult<UserInfo>.Ok(userInfo));

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new PoolFunction(Logger, auth, Context);

        var req = new PoolCreate(Name: _poolName, Os.Linux, Architecture.x86_64, true);
        var result = await func.Run(TestHttpRequestData.FromJson("POST", req));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);

        // should get an error back
        var returnedPool = BodyAs<Error>(result);
        Assert.Contains(returnedPool.Errors, c => c == "pool with that name already exists");
    }
}
