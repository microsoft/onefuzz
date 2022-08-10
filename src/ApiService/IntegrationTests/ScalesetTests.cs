using System;
using System.Linq;
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

    [Theory]
    [InlineData("POST", RequestType.Agent)]
    [InlineData("POST", RequestType.NoAuthorization)]
    [InlineData("PATCH", RequestType.Agent)]
    [InlineData("PATCH", RequestType.NoAuthorization)]
    [InlineData("GET", RequestType.Agent)]
    [InlineData("GET", RequestType.NoAuthorization)]
    [InlineData("DELETE", RequestType.Agent)]
    [InlineData("DELETE", RequestType.NoAuthorization)]
    public async Async.Task UserAuthorization_IsRequired(string method, RequestType authType) {
        var auth = new TestEndpointAuthorization(authType, Logger, Context);
        var func = new ScalesetFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.Empty(method));
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Async.Task Search_SpecificScaleset_ReturnsErrorIfNoneFound() {
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        var req = new ScalesetSearch(ScalesetId: Guid.NewGuid());
        var func = new ScalesetFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var err = BodyAs<Error>(result);
        Assert.Equal("unable to find scaleset", err.Errors?.Single());
    }

    [Fact]
    public async Async.Task Search_AllScalesets_ReturnsEmptyIfNoneFound() {
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        var req = new ScalesetSearch();
        var func = new ScalesetFunction(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("[]", BodyAsString(result));
    }
}
