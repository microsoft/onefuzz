
using System;
using System.Net;
using Microsoft.OneFuzz.Service;
using Tests.Fakes;
using Xunit;
using Xunit.Abstractions;

using Async = System.Threading.Tasks;

namespace Tests.Functions;

[Trait("Category", "Integration")]
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
    public async Async.Task TestInfo_WithoutAuthorization_IsRejected() {
        var auth = new TestEndpointAuthorization(RequestType.NoAuthorization, Logger, Context);
        var func = new Info(auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty("GET"));
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Async.Task TestInfo_WithAgentCredentials_IsRejected() {
        var auth = new TestEndpointAuthorization(RequestType.Agent, Logger, Context);
        var func = new Info(auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty("GET"));
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Async.Task TestInfo_WithUserCredentials_Succeeds() {
        // store the instance ID in the expected location:
        // for production this is done by the deploy script
        var instanceId = Guid.NewGuid().ToString();
        var containerClient = GetContainerClient("base-config");
        await containerClient.CreateAsync();
        await containerClient.GetBlobClient("instance_id").UploadAsync(new BinaryData(instanceId));

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new Info(auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty("GET"));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // the instance ID should be somewhere in the result,
        // indicating it was read from the blob
        Assert.Contains(instanceId, BodyAsString(result));
    }
}
