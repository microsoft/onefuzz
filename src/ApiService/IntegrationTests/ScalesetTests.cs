using System;
using System.Collections.Generic;
using System.Net;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;
using ScalesetFunction = Microsoft.OneFuzz.Service.Functions.Scaleset;

namespace IntegrationTests.Functions;

[Trait("Category", "Live")]
public class AzureStorageScalesetTest : ScalesetTestBase {
    public AzureStorageScalesetTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteScalesetTest : ScalesetTestBase {
    public AzuriteScalesetTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class ScalesetTestBase : FunctionTestBase {
    public ScalesetTestBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) { }

    [Fact]
    public async Async.Task Search_SpecificScaleset_ReturnsErrorIfNoneFound() {
        var req = new ScalesetSearch(ScalesetId: ScalesetId.Parse(Guid.NewGuid().ToString()));
        var func = new ScalesetFunction(LoggerProvider.CreateLogger<ScalesetFunction>(), Context);
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var err = BodyAs<ProblemDetails>(result);
        Assert.Equal("unable to find scaleset", err.Detail);
    }

    [Fact]
    public async Async.Task Search_AllScalesets_ReturnsEmptyIfNoneFound() {
        var req = new ScalesetSearch();
        var func = new ScalesetFunction(LoggerProvider.CreateLogger<ScalesetFunction>(), Context);
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("[]", BodyAsString(result));
    }

    [Fact]
    public async Async.Task Search_CanFindScaleset_AndReturnsNodes() {
        var scalesetId = ScalesetId.Parse(Guid.NewGuid().ToString());
        var poolName = PoolName.Parse($"pool-${Guid.NewGuid()}");
        var poolId = Guid.NewGuid();

        await Context.InsertAll(
            new Pool(poolName, poolId, Os.Linux, Managed: true, Architecture.x86_64, PoolState.Running),
            // scaleset to be found must exist
            new Scaleset(poolName, scalesetId, ScalesetState.Running, "", ImageReference.MustParse("x:y:z:v"), Region.Parse("region"), 1, null, false, false, new Dictionary<string, string>()),
            // some nodes
            new Node(poolName, Guid.NewGuid(), poolId, "version", ScalesetId: scalesetId),
            new Node(poolName, Guid.NewGuid(), poolId, "version", ScalesetId: scalesetId)
        );

        var req = new ScalesetSearch(ScalesetId: scalesetId);
        var func = new ScalesetFunction(LoggerProvider.CreateLogger<ScalesetFunction>(), Context);
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var resp = BodyAs<ScalesetResponse>(result);
        Assert.Equal(scalesetId, resp.ScalesetId);
        Assert.Equal(2, resp.Nodes?.Count);
    }

    [Fact]
    public async Async.Task Create_Scaleset() {
        var poolName = PoolName.Parse("mypool");
        await Context.InsertAll(
            // config must exist
            new InstanceConfig(Context.ServiceConfiguration.OneFuzzInstanceName!),
            // pool must exist and be managed
            new Pool(poolName, Guid.NewGuid(), Os.Linux, Managed: true, Architecture.x86_64, PoolState.Running));

        var req = new ScalesetCreate(
            poolName,
            TestVmssOperations.TestSku,
            TestVmssOperations.TestImage,
            Region: null,
            Size: 1,
            SpotInstances: false,
            Tags: new Dictionary<string, string>());

        var func = new ScalesetFunction(LoggerProvider.CreateLogger<ScalesetFunction>(), Context);
        var result = await func.Admin(TestHttpRequestData.FromJson("POST", req));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var resp = BodyAs<ScalesetResponse>(result);
        Assert.Equal(poolName, resp.PoolName);

        // auth should not be included in the response
        Assert.Null(resp.Auth);
    }

    [Fact]
    public async Async.Task Create_Scaleset_Under_NonExistent_Pool_Provides_Error() {
        var poolName = PoolName.Parse("nosuchpool");
        // pool not created
        var req = new ScalesetCreate(
            poolName,
            TestVmssOperations.TestSku,
            TestVmssOperations.TestImage,
            Region: null,
            Size: 1,
            SpotInstances: false,
            Tags: new Dictionary<string, string>());

        var func = new ScalesetFunction(LoggerProvider.CreateLogger<ScalesetFunction>(), Context);
        var result = await func.Admin(TestHttpRequestData.FromJson("POST", req));

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);

        var body = BodyAs<ProblemDetails>(result);
        Assert.Equal(ErrorCode.INVALID_REQUEST.ToString(), body.Title);
        Assert.Contains("unable to find pool with name nosuchpool", body.Detail);
    }
}
