
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Azure.Storage.Blobs;
using FluentAssertions;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Functions;
using Xunit;
using Xunit.Abstractions;

using Async = System.Threading.Tasks;

namespace IntegrationTests;

[Trait("Category", "Live")]
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

    [Fact]
    public async Async.Task CanDelete_IfCreatedThroughAPI() {
        var containerName = Container.Parse("test");
        var client = GetContainerClient(containerName);

        var func = new ContainersFunction(LoggerProvider.CreateLogger<ContainersFunction>(), Context);

        // create the container through API
        {
            var msg = TestHttpRequestData.FromJson("POST", new ContainerCreate(containerName));
            var result = await func.Run(msg);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        }

        // check underlying storage
        Assert.True(await client.ExistsAsync());

        // ensure it exists through API
        {
            var msg = TestHttpRequestData.FromJson("GET", new ContainerGet(containerName));
            var result = await func.Run(msg);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        }

        // delete through API
        {
            var msg = TestHttpRequestData.FromJson("DELETE", new ContainerDelete(containerName));
            var result = await func.Run(msg);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var response = BodyAs<BoolResult>(result);
            Assert.Equal(new BoolResult(true), response);
        }

        // ensure it is gone through API
        {
            var msg = TestHttpRequestData.FromJson("GET", new ContainerGet(containerName));
            var result = await func.Run(msg);
            // TODO: we should change this response code when revving API
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        }

        // container should be gone
        Assert.False(await client.ExistsAsync());
    }


    [Fact]
    public async Async.Task CanDelete_IfNotCreatedThroughAPI() {
        var func = new ContainersFunction(LoggerProvider.CreateLogger<ContainersFunction>(), Context);

        var containerName = Container.Parse("test");
        var client = GetContainerClient(containerName);

        // create container outside of API
        _ = await client.CreateIfNotExistsAsync();

        // delete through API
        {
            var msg = TestHttpRequestData.FromJson("DELETE", new ContainerDelete(containerName));
            var result = await func.Run(msg);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var response = BodyAs<BoolResult>(result);
            Assert.Equal(new BoolResult(true), response);
        }

        // ensure it is gone through API
        {
            var msg = TestHttpRequestData.FromJson("GET", new ContainerGet(containerName));
            var result = await func.Run(msg);
            // TODO: we should change this response code when revving API
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        }

        // container should be gone
        Assert.False(await client.ExistsAsync());
    }

    [Fact]
    public async Async.Task CanPost_New() {
        var meta = new Dictionary<string, string> { { "some", "value" } };
        var containerName = Container.Parse("test");
        var msg = TestHttpRequestData.FromJson("POST", new ContainerCreate(containerName, null, meta));

        var func = new ContainersFunction(LoggerProvider.CreateLogger<ContainersFunction>(), Context);
        var result = await func.Run(msg);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // container should be created with metadata:
        var client = GetContainerClient(containerName);
        Assert.True(await client.ExistsAsync());
        var props = await client.GetPropertiesAsync();
        Assert.Equal(meta, props.Value.Metadata);

        var response = BodyAs<ContainerInfo>(result);
        await AssertCanCRUD(response.SasUrl);
    }

    [Fact]
    public async Async.Task CanPost_Existing() {
        var containerName = Container.Parse("test");
        var client = GetContainerClient(containerName);
        _ = await client.CreateIfNotExistsAsync();

        var metadata = new Dictionary<string, string> { { "some", "value" } };
        var msg = TestHttpRequestData.FromJson("POST", new ContainerCreate(containerName, null, metadata));

        var func = new ContainersFunction(LoggerProvider.CreateLogger<ContainersFunction>(), Context);
        var result = await func.Run(msg);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // metadata should _not_ be updated:
        var props = await client.GetPropertiesAsync();
        Assert.Empty(props.Value.Metadata);

        var response = BodyAs<ContainerInfo>(result);
        await AssertCanCRUD(response.SasUrl);
    }


    [Fact]
    public async Async.Task Get_Existing() {
        var containerName = Container.Parse("test");
        {
            var client = GetContainerClient(containerName);
            _ = await client.CreateIfNotExistsAsync();
        }

        var msg = TestHttpRequestData.FromJson("GET", new ContainerGet(containerName));

        var func = new ContainersFunction(LoggerProvider.CreateLogger<ContainersFunction>(), Context);
        var result = await func.Run(msg);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // we should get back a SAS URI that works (create, delete, list, read):
        var info = BodyAs<ContainerInfo>(result);
        await AssertCanCRUD(info.SasUrl);
    }

    [Fact]
    public async Async.Task Get_Missing_Fails() {
        var container = Container.Parse("container");
        var msg = TestHttpRequestData.FromJson("GET", new ContainerGet(container));

        var func = new ContainersFunction(LoggerProvider.CreateLogger<ContainersFunction>(), Context);
        var result = await func.Run(msg);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Async.Task List_Existing() {
        var meta1 = new Dictionary<string, string> { { "key1", "value1" } };
        var meta2 = new Dictionary<string, string> { { "key2", "value2" } };
        _ = await GetContainerClient(Container.Parse("one")).CreateIfNotExistsAsync(metadata: meta1);
        _ = await GetContainerClient(Container.Parse("two")).CreateIfNotExistsAsync(metadata: meta2);

        var msg = TestHttpRequestData.Empty("GET"); // this means list all

        var func = new ContainersFunction(LoggerProvider.CreateLogger<ContainersFunction>(), Context);
        var result = await func.Run(msg);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var list = BodyAs<ContainerInfoBase[]>(result);
        // other tests can run in parallel, so filter to just our containers:
        var cs = list
            .Where(ci => ci.Name.String.StartsWith(Context.ServiceConfiguration.OneFuzzStoragePrefix))
            .ToList();

        _ = list.Should().Contain(ci => ci.Name.String.Contains("one"));
        _ = list.Should().Contain(ci => ci.Name.String.Contains("two"));

        var cs1 = list.Single(ci => ci.Name.String.Contains("one"));
        var cs2 = list.Single(ci => ci.Name.String.Contains("two"));

        // ensure correct metadata was returned.
        // these will be in order as "one"<"two"
        Assert.Equal(meta1, cs1.Metadata);
        Assert.Equal(meta2, cs2.Metadata);
    }

    private static async Async.Task AssertCanCRUD(Uri sasUrl) {
        var client = new BlobContainerClient(sasUrl);
        _ = await client.UploadBlobAsync("blob", new BinaryData("content")); // create
        var b = Assert.Single(await client.GetBlobsAsync().ToListAsync()); // list
        using (var s = await client.GetBlobClient(b.Name).OpenReadAsync())
        using (var sr = new StreamReader(s)) {
            Assert.Equal("content", await sr.ReadToEndAsync()); // read
        }
        using var r = await client.DeleteBlobAsync("blob"); // delete
    }

    [Fact]
    public async Async.Task BadContainerNameProducesGoodErrorMessage() {
        // use anonymous type so we can send an invalid name
        var msg = TestHttpRequestData.FromJson("POST", new { Name = "AbCd" });

        var func = new ContainersFunction(LoggerProvider.CreateLogger<ContainersFunction>(), Context);
        var result = await func.Run(msg);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);

        var details = BodyAs<ProblemDetails>(result);
        Assert.Equal(ErrorCode.INVALID_REQUEST.ToString(), details.Title);
        Assert.StartsWith("Unable to parse 'AbCd' as a Container: Container name must", details.Detail);
    }

    // The APIs used by this test (filter by tag) aren't available in azurite yet
    // https://github.com/Azure/Azurite/issues/647
    [Trait("Category", "Live")]
    [Fact]
    public async Async.Task DeleteExpiredBlobsDoesNotTouchUntaggedBlobs() {
        var testContainer = Container.Parse("testblobretention");
        var client = GetContainerClient(testContainer);
        var expirableBlobName = "expirableBlob";
        var nonExpirableBlobName = "nonExpirableBlob";

        TestFeatureManagerSnapshot.AddFeatureFlag(FeatureFlagConstants.EnableDryRunBlobRetention, enabled: false);

        _ = await Context.Containers.CreateContainer(testContainer, StorageType.Corpus, null, null);
        await Context.Containers.SaveBlob(testContainer, expirableBlobName, string.Empty, StorageType.Corpus, DateOnly.MinValue);
        await Context.Containers.SaveBlob(testContainer, nonExpirableBlobName, string.Empty, StorageType.Corpus);

        var retentionPolicyTagKey = RetentionPolicyUtils.CreateExpiryDateTag(DateOnly.MinValue).Key;

        _ = client.GetBlobClient(expirableBlobName).GetTags().Value.Tags
            .Should().ContainKey(retentionPolicyTagKey);

        client.GetBlobClient(nonExpirableBlobName).GetTags().Value.Tags
            .Should().NotContainKey(retentionPolicyTagKey);

        await Context.Containers.DeleteAllExpiredBlobs();

        client.GetBlobClient(expirableBlobName).Exists().Value.Should().BeFalse();
        client.GetBlobClient(nonExpirableBlobName).Exists().Value.Should().BeTrue();
    }
}
