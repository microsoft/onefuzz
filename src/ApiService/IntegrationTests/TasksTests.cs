using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntegrationTests.Fakes;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.Functions;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Xunit;
using Xunit.Abstractions;
using Async = System.Threading.Tasks;

namespace IntegrationTests.Functions;

[Trait("Category", "Live")]
public class AzureStorageTasksTest : TasksTestBase {
    public AzureStorageTasksTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment()) { }
}

public class AzuriteTasksTest : TasksTestBase {
    public AzuriteTasksTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage()) { }
}

public abstract class TasksTestBase : FunctionTestBase {
    public TasksTestBase(ITestOutputHelper output, IStorage storage)
        : base(output, storage) { }

    [Fact]
    public async Async.Task SpecifyingVmIsNotPermitted() {
        var func = new Tasks(Context);

        var req = new TaskCreate(
            Guid.NewGuid(),
            null,
            new TaskDetails(TaskType.DotnetCoverage, 100),
            new TaskPool(1, PoolName.Parse("pool")));

        // the 'vm' property used to be permitted but is no longer, add it:
        var serialized = (JsonObject?)JsonSerializer.SerializeToNode(req, EntityConverter.GetJsonSerializerOptions());
        serialized!["vm"] = new JsonObject { { "fake", 1 } };
        var testData = new TestHttpRequestData("POST", new BinaryData(JsonSerializer.SerializeToUtf8Bytes(serialized, EntityConverter.GetJsonSerializerOptions())));
        var ctx = new TestFunctionContext();
        var result = await func.Run(testData, ctx);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var err = BodyAs<ProblemDetails>(result);
        Assert.Equal("Unexpected property: \"vm\"", err.Detail);
    }

    [Fact]
    public async Async.Task PoolIsRequired() {
        var func = new Tasks(Context);

        // override the found user credentials - need these to store user
        var ctx = new TestFunctionContext();
        ctx.SetUserAuthInfo(new UserInfo(ApplicationId: Guid.NewGuid(), ObjectId: Guid.NewGuid(), "upn"));

        var req = new TaskCreate(
            Guid.NewGuid(),
            null,
            new TaskDetails(TaskType.DotnetCoverage, 100),
            null! /* <- here */);

        var result = await func.Run(TestHttpRequestData.FromJson("POST", req), ctx);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var err = BodyAs<ProblemDetails>(result);
        Assert.Equal("The Pool field is required.", err.Detail);
    }

    [Fact]
    public async Async.Task CanSearchWithJobIdAndEmptyListOfStates() {
        var req = new TaskSearch(
            JobId: Guid.NewGuid(),
            TaskId: null,
            State: new List<TaskState>());

        var func = new Tasks(Context);
        var ctx = new TestFunctionContext();
        var result = await func.Run(TestHttpRequestData.FromJson("GET", req), ctx);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }
}
