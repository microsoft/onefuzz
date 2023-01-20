using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Functions;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;

namespace IntegrationTests.Functions;

[Trait("Category", "Live")]
public class AzureStorageReproVmssTest : ReproVmssTestBase {
    public AzureStorageReproVmssTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteReproVmssTest : ReproVmssTestBase {
    public AzuriteReproVmssTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class ReproVmssTestBase : FunctionTestBase {
    public ReproVmssTestBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) { }


    [Theory]
    [InlineData("POST", RequestType.Agent)]
    [InlineData("POST", RequestType.NoAuthorization)]
    [InlineData("GET", RequestType.Agent)]
    [InlineData("GET", RequestType.NoAuthorization)]
    [InlineData("DELETE", RequestType.Agent)]
    [InlineData("DELETE", RequestType.NoAuthorization)]
    public async Async.Task UserAuthorization_IsRequired(string method, RequestType authType) {
        var auth = new TestEndpointAuthorization(authType, Logger, Context);
        var func = new ReproVmss(Logger, auth, Context);
        var result = await func.Run(TestHttpRequestData.Empty(method));
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Async.Task GetMissingVmFails() {
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new ReproVmss(Logger, auth, Context);
        var req = new ReproGet(VmId: Guid.NewGuid());
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));
        // TODO: should this be 404?
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var err = BodyAs<ProblemDetails>(result);
        Assert.Equal("no such VM", err.Detail);
    }

    [Fact]
    public async Async.Task GetAvailableVMsCanReturnEmpty() {
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new ReproVmss(Logger, auth, Context);
        var req = new ReproGet(VmId: null); // this means "all available"
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Empty(BodyAs<Repro[]>(result));
    }

    [Fact]
    public async Async.Task GetAvailableVMsCanReturnVM() {
        var vmId = Guid.NewGuid();

        await Context.InsertAll(
            new Repro(
                VmId: vmId,
                TaskId: Guid.NewGuid(),
                new ReproConfig(Container.Parse("abcd"), "", 12345),
                Auth: null,
                Os: Os.Linux));

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new ReproVmss(Logger, auth, Context);
        var req = new ReproGet(VmId: null); // this means "all available"
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var repro = Assert.Single(BodyAs<Repro[]>(result));
        Assert.Equal(vmId, repro.VmId);
    }

    [Fact]
    public async Async.Task GetAvailableVMsCanReturnSpecificVM() {
        var vmId = Guid.NewGuid();

        await Context.InsertAll(
            new Repro(
                VmId: vmId,
                TaskId: Guid.NewGuid(),
                new ReproConfig(Container.Parse("abcd"), "", 12345),
                Auth: null,
                Os: Os.Linux));

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new ReproVmss(Logger, auth, Context);
        var req = new ReproGet(VmId: vmId);
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(vmId, BodyAs<Repro>(result).VmId);
    }


    [Fact]
    public async Async.Task GetAvailableVMsDoesNotReturnUnavailableVMs() {
        await Context.InsertAll(
            new Repro(
                VmId: Guid.NewGuid(),
                TaskId: Guid.NewGuid(),
                new ReproConfig(Container.Parse("abcd"), "", 12345),
                Auth: null,
                Os: Os.Linux,
                State: VmState.Stopping),
            new Repro(
                VmId: Guid.NewGuid(),
                TaskId: Guid.NewGuid(),
                new ReproConfig(Container.Parse("abcd"), "", 12345),
                Auth: null,
                Os: Os.Linux,
                State: VmState.Stopped));

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);
        var func = new ReproVmss(Logger, auth, Context);
        var req = new ReproGet(VmId: null); // this means "all available"
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Empty(BodyAs<Repro[]>(result));
    }

    [Fact]
    public async Async.Task CannotCreateVMWithoutCredentials() {
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        var func = new ReproVmss(Logger, auth, Context);
        var req = new ReproCreate(Container.Parse("abcd"), "/", 12345);
        var result = await func.Run(TestHttpRequestData.FromJson("POST", req));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var err = BodyAs<ProblemDetails>(result);
        Assert.Equal(new ProblemDetails(400, "INVALID_REQUEST", "unable to find authorization token"), err);
    }

    [Fact]
    public async Async.Task CannotCreateVMForMissingReport() {
        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        // setup fake user
        var userInfo = new UserInfo(Guid.NewGuid(), Guid.NewGuid(), "upn");
        Context.UserCredentials = new TestUserCredentials(Logger, Context.ConfigOperations, OneFuzzResult.Ok(userInfo));

        var func = new ReproVmss(Logger, auth, Context);
        var req = new ReproCreate(Container.Parse("abcd"), "/", 12345);
        var result = await func.Run(TestHttpRequestData.FromJson("POST", req));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var err = BodyAs<ProblemDetails>(result);
        Assert.Equal(new ProblemDetails(400, "UNABLE_TO_FIND", "unable to find report"), err);
    }

    private async Async.Task<(Container, string)> CreateContainerWithReport(Guid jobId, Guid taskId) {
        var container = Container.Parse(Guid.NewGuid().ToString("N"));
        var filename = "report.json";
        // Setup container with Report
        var cc = GetContainerClient(container);
        _ = await cc.CreateIfNotExistsAsync();
        using (var ms = new MemoryStream()) {
            var emptyReport = new Report(
                null,
                null,
                "",
                "",
                "",
                new List<string>(),
                "",
                "",
                null,
                TaskId: taskId,
                JobId: jobId,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null
                );

            JsonSerializer.Serialize(ms, emptyReport, EntityConverter.GetJsonSerializerOptions());
            _ = ms.Seek(0, SeekOrigin.Begin);
            _ = await cc.UploadBlobAsync(filename, ms);
        }

        return (container, filename);
    }

    [Fact]
    public async Async.Task CannotCreateVMForMissingTask() {
        var (container, filename) = await CreateContainerWithReport(Guid.NewGuid(), Guid.NewGuid());

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        // setup fake user
        var userInfo = new UserInfo(Guid.NewGuid(), Guid.NewGuid(), "upn");
        Context.UserCredentials = new TestUserCredentials(Logger, Context.ConfigOperations, OneFuzzResult.Ok(userInfo));

        var func = new ReproVmss(Logger, auth, Context);
        var req = new ReproCreate(container, filename, 12345);
        var result = await func.Run(TestHttpRequestData.FromJson("POST", req));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var err = BodyAs<ProblemDetails>(result);
        Assert.Equal(new ProblemDetails(400, "INVALID_REQUEST", "unable to find task"), err);
    }

    [Fact]
    public async Async.Task CanCreateVMSuccessfully() {
        // report must have TaskID pointing to a valid Task

        var jobId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var (container, filename) = await CreateContainerWithReport(jobId: jobId, taskId: taskId);
        await Context.InsertAll(
            new Task(
                JobId: jobId,
                TaskId: taskId,
                TaskState.Running,
                Os.Linux,
                new TaskConfig(
                    JobId: jobId,
                    null,
                    new TaskDetails(TaskType.LibfuzzerFuzz, 12345))));

        var auth = new TestEndpointAuthorization(RequestType.User, Logger, Context);

        // setup fake user
        var userInfo = new UserInfo(Guid.NewGuid(), Guid.NewGuid(), "upn");
        Context.UserCredentials = new TestUserCredentials(Logger, Context.ConfigOperations, OneFuzzResult.Ok(userInfo));

        var func = new ReproVmss(Logger, auth, Context);
        var req = new ReproCreate(container, filename, 12345);
        var result = await func.Run(TestHttpRequestData.FromJson("POST", req));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var repro = BodyAs<Repro>(result);
        Assert.Equal(taskId, repro.TaskId);
    }
}
