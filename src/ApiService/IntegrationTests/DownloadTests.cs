
using System;
using System.Net;
using System.Net.Http;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Functions;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;

namespace IntegrationTests;

[Trait("Category", "Live")]
public class AzureStorageDownloadTest : DownloadTestBase {
    public AzureStorageDownloadTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteDownloadTest : DownloadTestBase {
    public AzuriteDownloadTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class DownloadTestBase : FunctionTestBase {
    public DownloadTestBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) { }

    [Fact]
    public async Async.Task Download_WithoutContainer_IsRejected() {
        var req = TestHttpRequestData.Empty("GET");
        var url = new UriBuilder(req.Url) { Query = "filename=xxx" }.Uri;
        req.SetUrl(url);

        var func = new Download(Context);
        var result = await func.Run(req);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);

        var err = BodyAs<ProblemDetails>(result);
        Assert.Equal(ErrorCode.INVALID_REQUEST.ToString(), err.Title);
    }

    [Fact]
    public async Async.Task Download_WithoutFilename_IsRejected() {
        var req = TestHttpRequestData.Empty("GET");
        var url = new UriBuilder(req.Url) { Query = "container=xxx" }.Uri;
        req.SetUrl(url);

        var func = new Download(Context);

        var result = await func.Run(req);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);

        var err = BodyAs<ProblemDetails>(result);
        Assert.Equal(ErrorCode.INVALID_REQUEST.ToString(), err.Title);
    }

    [Fact]
    public async Async.Task Container_NotFound_Generates404() {
        var req = TestHttpRequestData.Empty("GET");
        // this container won't exist because we haven't explicitly created it
        var url = new UriBuilder(req.Url) { Query = "container=xxx&filename=yyy" }.Uri;
        req.SetUrl(url);

        var func = new Download(Context);

        var result = await func.Run(req);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);

        var err = BodyAs<ProblemDetails>(result);
        Assert.Equal(ErrorCode.INVALID_CONTAINER.ToString(), err.Title);
    }

    [Fact]
    public async Async.Task Download_RedirectsToResult_WithLocationHeader() {
        // set up a file to download
        var containerName = Container.Parse("xxx");
        var container = GetContainerClient(containerName);
        _ = await container.CreateAsync();
        _ = await container.UploadBlobAsync("yyy", new BinaryData("content"));

        var req = TestHttpRequestData.Empty("GET");
        var url = new UriBuilder(req.Url) { Query = "container=xxx&filename=yyy" }.Uri;
        req.SetUrl(url);

        var func = new Download(Context);

        var result = await func.Run(req);
        Assert.Equal(HttpStatusCode.Found, result.StatusCode);
        var location = Assert.Single(result.Headers.GetValues("Location"));

        // check that the SAS URI works
        using var client = new HttpClient();
        var blobContent = await client.GetStringAsync(location);
        Assert.Equal("content", blobContent);
    }
}
