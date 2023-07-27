using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Functions;
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

    [Fact]
    public async Async.Task Delete_NonExistentJob_Fails() {
        var func = new Jobs(Context, LoggerProvider.CreateLogger<Jobs>());

        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("DELETE", new JobGet(_jobId)), ctx);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);

        var err = BodyAs<ProblemDetails>(result);
        Assert.Equal(ErrorCode.INVALID_JOB.ToString(), err.Title);
    }

    [Fact]
    public async Async.Task Delete_ExistingJob_SetsStoppingState() {
        await Context.InsertAll(
            new Job(_jobId, JobState.Enabled, _config, null));

        var func = new Jobs(Context, LoggerProvider.CreateLogger<Jobs>());

        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("DELETE", new JobGet(_jobId)), ctx);
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
            new Job(_jobId, JobState.Stopped, _config, null));

        var func = new Jobs(Context, LoggerProvider.CreateLogger<Jobs>());

        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("DELETE", new JobGet(_jobId)), ctx);
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
            new Job(_jobId, JobState.Stopped, _config, null));

        var func = new Jobs(Context, LoggerProvider.CreateLogger<Jobs>());

        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("GET", new JobSearch(JobId: _jobId)), ctx);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var response = BodyAs<JobResponse>(result);
        Assert.Equal(_jobId, response.JobId);
        Assert.Equal(JobState.Stopped, response.State);
    }


    [Fact]
    public async Async.Task Get_ReturnsUserData() {
        var userInfo = new StoredUserInfo(Guid.NewGuid(), Guid.NewGuid());

        await Context.InsertAll(
            new Job(_jobId, JobState.Stopped, _config, userInfo));

        var func = new Jobs(Context, LoggerProvider.CreateLogger<Jobs>());

        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("GET", new JobSearch(JobId: _jobId)), ctx);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var response = BodyAs<JobResponse>(result);
        Assert.Equal(_jobId, response.JobId);
        Assert.Equal(userInfo, response.UserInfo);
    }

    [Fact]
    public async Async.Task Get_CanFindJobsInState() {
        await Context.InsertAll(
            new Job(Guid.NewGuid(), JobState.Init, _config, null),
            new Job(Guid.NewGuid(), JobState.Stopping, _config, null),
            new Job(Guid.NewGuid(), JobState.Enabled, _config, null),
            new Job(Guid.NewGuid(), JobState.Stopped, _config, null));

        var func = new Jobs(Context, LoggerProvider.CreateLogger<Jobs>());

        var req = new JobSearch(State: new List<JobState> { JobState.Enabled });
        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req), ctx);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var response = BodyAs<JobResponse[]>(result);
        Assert.Equal(JobState.Enabled, response.Single().State);
    }

    [Fact]
    public async Async.Task Get_CanFindMultipleJobsInState() {
        await Context.InsertAll(
            new Job(Guid.NewGuid(), JobState.Init, _config, null),
            new Job(Guid.NewGuid(), JobState.Stopping, _config, null),
            new Job(Guid.NewGuid(), JobState.Enabled, _config, null),
            new Job(Guid.NewGuid(), JobState.Stopped, _config, null));

        var func = new Jobs(Context, LoggerProvider.CreateLogger<Jobs>());

        var req = new JobSearch(State: new List<JobState> { JobState.Enabled, JobState.Stopping });
        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req), ctx);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var response = BodyAs<JobResponse[]>(result);
        Assert.Equal(2, response.Length);
        Assert.Contains(response, j => j.State == JobState.Stopping);
        Assert.Contains(response, j => j.State == JobState.Enabled);
    }

    [Fact]
    public async Async.Task Post_CreatesJob_AndContainer() {
        var func = new Jobs(Context, LoggerProvider.CreateLogger<Jobs>());

        // need user credentials to put into the job object
        var ctx = new TestFunctionContext();
        ctx.SetUserAuthInfo(new UserInfo(Guid.NewGuid(), Guid.NewGuid(), "upn"));
        var result = await func.Run(TestHttpRequestData.FromJson("POST", _config), ctx);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var job = Assert.Single(await Context.JobOperations.SearchAll().ToListAsync());
        var response = BodyAs<JobResponse>(result);
        Assert.Equal(job.JobId, response.JobId);
        Assert.NotNull(job.Config.Logs);
        Assert.Empty(new Uri(job.Config.Logs!).Query);

        var container = Assert.Single(await Context.Containers.GetContainers(StorageType.Corpus), c => c.Key.String.Contains(job.JobId.ToString()));
        var metadata = Assert.Single(container.Value);
        Assert.Equal(new KeyValuePair<string, string>("container_type", "logs"), metadata);
    }


    [Fact]
    public async Async.Task Get_CanFindSpecificJobWithTaskInfo() {

        var taskConfig = new TaskConfig(_jobId, new List<Guid>(), new TaskDetails(TaskType.Coverage, 60));
        var task = new Task(_jobId, Guid.NewGuid(), TaskState.Running, Os.Windows, taskConfig);

        await Context.InsertAll(
            new Job(_jobId, JobState.Stopped, _config, null), task);

        var func = new Jobs(Context, LoggerProvider.CreateLogger<Jobs>());

        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("GET", new JobSearch(JobId: _jobId, WithTasks: false)), ctx);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var response = BodyAs<JobResponse>(result);
        Assert.Equal(_jobId, response.JobId);
        Assert.NotNull(response.TaskInfo);
        var returnedTasks = response.TaskInfo.OfType<JobTaskInfo>().ToList();
        Assert.NotEmpty(returnedTasks);
        Assert.Equal(task.TaskId, returnedTasks[0].TaskId);
        Assert.Equal(task.State, returnedTasks[0].State);
        Assert.Equal(task.Config.Task.Type, returnedTasks[0].Type);
    }

    [Fact]
    public async Async.Task Get_CanFindSpecificJobWithFullTask() {
        var taskConfig = new TaskConfig(_jobId, new List<Guid>(), new TaskDetails(TaskType.Coverage, 60));
        var task = new Task(_jobId, Guid.NewGuid(), TaskState.Running, Os.Windows, taskConfig);

        await Context.InsertAll(
            new Job(_jobId, JobState.Stopped, _config, null), task);

        var func = new Jobs(Context, LoggerProvider.CreateLogger<Jobs>());

        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("GET", new JobSearch(JobId: _jobId, WithTasks: true)), ctx);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var response = BodyAs<JobResponse>(result);
        Assert.Equal(_jobId, response.JobId);
        Assert.NotNull(response.TaskInfo);
        var returnedTasks = response.TaskInfo.OfType<Task>().ToList();
        Assert.NotEmpty(returnedTasks);
        Assert.Equal(task.TaskId, returnedTasks[0].TaskId);
        Assert.Equal(task.State, returnedTasks[0].State);
        Assert.Equal(task.Config.Task.Type, returnedTasks[0].Type);

    }
}
