

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

    private readonly Guid _poolId = Guid.NewGuid();
    private readonly PoolName _poolName = PoolName.Parse("pool-" + Guid.NewGuid());


    [Theory]
    [InlineData("POST", RequestType.Agent)]
    [InlineData("POST", RequestType.NoAuthorization)]
    [InlineData("GET", RequestType.Agent)]
    [InlineData("GET", RequestType.NoAuthorization)]
    [InlineData("PATCH", RequestType.Agent)]
    [InlineData("PATCH", RequestType.NoAuthorization)]
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

        Assert.Empty(BodyAsString(result));
    }

    [Fact]
    public async Async.Task Search_SpecificPool_NoQuery_ReturnsBadRequest() {
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        var func = new PoolFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.FromJson("GET", new PoolSearch()));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);

        var err = BodyAs<Error>(result);
        Assert.Contains(err.Errors, c => c.Contains("at least one search option"));
    }
}
