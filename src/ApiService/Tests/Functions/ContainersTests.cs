
using System;
using System.Net;
using System.Net.Http;
using Microsoft.OneFuzz.Service;
using Tests.Fakes;
using Xunit;
using Xunit.Abstractions;

using Async = System.Threading.Tasks;

namespace Tests.Functions;

[Trait("Category", "Integration")]
public class AzureStorageContainersTest : ContainersTestBase {
    public AzureStorageContainersTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteContainersTest : ContainersTestBase {
    public AzuriteContainersTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class ContainersTestBase : FunctionTestBase {
    public ContainersTestBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) { }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Async.Task WithoutAuthorization_IsRejected(string method) {
        var auth = new TestEndpointAuthorization(RequestType.NoAuthorization, Logger, Context);
        var func = new ContainersFunction(Logger, auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty(method));
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);

        var err = BodyAs<Error>(result);
        Assert.Equal(ErrorCode.UNAUTHORIZED, err.Code);
    }

}
