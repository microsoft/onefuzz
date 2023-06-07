
using System.Net;
using FluentAssertions;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Functions;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;

namespace IntegrationTests;

[Trait("Category", "Live")]
public class AzureStorageInfoTest : InfoTestBase {
    public AzureStorageInfoTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteInfoTest : InfoTestBase {
    public AzuriteInfoTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class InfoTestBase : FunctionTestBase {
    public InfoTestBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) { }

    [Fact]
    public async Async.Task TestInfo_Succeeds() {
        var func = new Info(Context);

        var result = await func.Run(TestHttpRequestData.Empty("GET"));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // the instance ID should be somewhere in the result,
        // indicating it was read from the blob
        var info = BodyAs<InfoResponse>(result);
        info.InstanceId.Should().NotBeEmpty();
    }
}
