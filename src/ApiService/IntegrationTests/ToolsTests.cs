using System;
using System.Linq;
using Microsoft.OneFuzz.Service;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;

namespace IntegrationTests;

//[Trait("Category", "Live")]
public class AzureStorageToolsTest : ToolsTestBase {
    public AzureStorageToolsTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteToolsTest : ToolsTestBase {
    public AzuriteToolsTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class ToolsTestBase : FunctionTestBase {
    private readonly IStorage _storage;

    public ToolsTestBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) {
        _storage = storage;
    }

    //[Fact]
    public async Async.Task Delete_ExistingStoppedJob_DoesNotSetStoppingState() {

        const int NUMBER_OF_FILES = 20;

        var toolsContainerClient = GetContainerClient(WellKnownContainers.Tools);
        _ = await toolsContainerClient.CreateIfNotExistsAsync();

        var files = Enumerable.Range(1, NUMBER_OF_FILES).Select((x, i) => (path: i, content: Guid.NewGuid())).ToList();


        foreach (var (path, content) in files) {
            var r = await toolsContainerClient.UploadBlobAsync(path.ToString(), BinaryData.FromString(content.ToString()));
            Assert.False(r.GetRawResponse().IsError);

        }




        // toolsContainerClient.
        //_storage.GetBlobServiceClientForAccountName(WellKnownContainers.Tools)
        // await Context.InsertAll(
        //     new Job(_jobId, JobState.Stopped, _config));
        //
        // var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        // var func = new Jobs(auth, Context, Logger);
        //
        // var result = await func.Run(TestHttpRequestData.FromJson("DELETE", new JobGet(_jobId)));
        // Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        //
        // var response = BodyAs<JobResponse>(result);
        // Assert.Equal(_jobId, response.JobId);
        // Assert.Equal(JobState.Stopped, response.State);
        //
        // var job = await Context.JobOperations.Get(_jobId);
        // Assert.Equal(JobState.Stopped, job?.State);
    }
}
