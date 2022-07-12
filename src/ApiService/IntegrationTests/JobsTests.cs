using System;
using System.Collections.Generic;
using System.Net;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;

namespace IntegrationTests;

[Trait("Category", "Live")]
public class AzureStorageJobsTest : JobsTestBase {
    public AzureStorageJobsTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteJobsTest : JobsTestBase {
    public AzuriteJobsTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class JobsTestBase : FunctionTestBase {
    public JobsTestBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) { }

    private readonly Guid _jobId = Guid.NewGuid();
    private readonly JobConfig _config = new("project", "name", "build", 1000, null);

    [Theory]
    [InlineData("POST")]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Async.Task Access_WithoutAuthorization_IsRejected(string method) {
        var auth = new TestEndpointAuthorization(RequestType.NoAuthorization, Logger, Context);
        var func = new Jobs(auth, Context);

        var result = await func.Run(TestHttpRequestData.Empty(method));
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);

        var err = BodyAs<Error>(result);
        Assert.Equal(ErrorCode.UNAUTHORIZED, err.Code);
    }

    [Fact]
    public async Async.Task Delete_NonExistentJob_Fails() {
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new Jobs(auth, Context);

        var result = await func.Run(TestHttpRequestData.FromJson("DELETE", new JobGet(_jobId)));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);

        var err = BodyAs<Error>(result);
        Assert.Equal(ErrorCode.INVALID_JOB, err.Code);
    }

    [Fact]
    public async Async.Task Delete_ExistingJob_SetsStoppingState() {
        await Context.InsertAll(
            new Job(_jobId, JobState.Enabled, _config));

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new Jobs(auth, Context);

        var result = await func.Run(TestHttpRequestData.FromJson("DELETE", new JobGet(_jobId)));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var response = BodyAs<JobResponse>(result);
        Assert.Equal(_jobId, response.JobId);
        Assert.Equal(JobState.Stopping, response.State);

        var job = await Context.JobOperations.Get(_jobId);
        Assert.Equal(JobState.Stopping, job?.State);
    }

    [Fact]
    public async Async.Task Delete_ExistingStoppedJob_DoesNotSetStoppingState() {
        await Context.InsertAll(
            new Job(_jobId, JobState.Stopped, _config));

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new Jobs(auth, Context);

        var result = await func.Run(TestHttpRequestData.FromJson("DELETE", new JobGet(_jobId)));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var response = BodyAs<JobResponse>(result);
        Assert.Equal(_jobId, response.JobId);
        Assert.Equal(JobState.Stopped, response.State);

        var job = await Context.JobOperations.Get(_jobId);
        Assert.Equal(JobState.Stopped, job?.State);
    }


    [Fact]
    public async Async.Task Get_CanFindSpecificJob() {
        await Context.InsertAll(
            new Job(_jobId, JobState.Stopped, _config));

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new Jobs(auth, Context);

        var result = await func.Run(TestHttpRequestData.FromJson("GET", new JobSearch(JobId: _jobId)));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var response = BodyAs<JobResponse>(result);
        Assert.Equal(_jobId, response.JobId);
        Assert.Equal(JobState.Stopped, response.State);
    }

    [Fact]
    public async Async.Task Get_CanFindJobsInState() {
        await Context.InsertAll(
            new Job(Guid.NewGuid(), JobState.Init, _config),
            new Job(Guid.NewGuid(), JobState.Stopping, _config),
            new Job(Guid.NewGuid(), JobState.Enabled, _config),
            new Job(Guid.NewGuid(), JobState.Stopped, _config));

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new Jobs(auth, Context);

        var req = new JobSearch(State: new List<JobState> { JobState.Enabled });
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var response = BodyAs<JobResponse>(result);
        Assert.Equal(JobState.Enabled, response.State);
    }

    [Fact]
    public async Async.Task Get_CanFindMultipleJobsInState() {
        await Context.InsertAll(
            new Job(Guid.NewGuid(), JobState.Init, _config),
            new Job(Guid.NewGuid(), JobState.Stopping, _config),
            new Job(Guid.NewGuid(), JobState.Enabled, _config),
            new Job(Guid.NewGuid(), JobState.Stopped, _config));

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new Jobs(auth, Context);

        var req = new JobSearch(State: new List<JobState> { JobState.Enabled, JobState.Stopping });
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var response = BodyAs<JobResponse[]>(result);
        Assert.Equal(2, response.Length);
        Assert.Contains(response, j => j.State == JobState.Stopping);
        Assert.Contains(response, j => j.State == JobState.Enabled);
    }
}
