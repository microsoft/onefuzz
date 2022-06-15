using System;
using System.Linq;
using System.Net;
using Microsoft.OneFuzz.Service;
using Tests.Fakes;
using Xunit;
using Xunit.Abstractions;

using Async = System.Threading.Tasks;

namespace Tests.Functions;

[Trait("Category", "Integration")]
public class AzureStorageAgentEventsTest : AgentEventsTestsBase {
    public AzureStorageAgentEventsTest(ITestOutputHelper output)
        : base(output, Integration.AzureStorage.FromEnvironment(), "UNUSED") { }
}

public class AzuriteAgentEventsTest : AgentEventsTestsBase {
    public AzuriteAgentEventsTest(ITestOutputHelper output)
        : base(output, new Integration.AzuriteStorage(), "devstoreaccount1") { }
}

public abstract class AgentEventsTestsBase : FunctionTestBase {
    public AgentEventsTestsBase(ITestOutputHelper output, IStorage storage, string accountId)
        : base(output, storage, accountId) { }

    // shared helper variables (per-test)
    readonly Guid jobId = Guid.NewGuid();
    readonly Guid taskId = Guid.NewGuid();
    readonly Guid machineId = Guid.NewGuid();
    readonly string poolName = $"pool-{Guid.NewGuid()}";
    readonly Guid poolId = Guid.NewGuid();
    readonly string poolVersion = $"version-{Guid.NewGuid()}";

    [Fact]
    public async Async.Task WorkerEventMustHaveDoneOrRunningSet() {
        var func = new AgentEvents(Logger, CreateTestContext());

        var data = new NodeStateEnvelope(
            Guid.NewGuid(),
            new WorkerEvent(null, null));

        var result = await func.Run(TestHttpRequestData.FromJson("POST", data));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }


    [Fact]
    public async Async.Task WorkerDone_WithSuccessfulResult_ForRunningTask_MarksTaskAsStopping() {
        var ctx = CreateTestContext();

        await ctx.InsertAll(
                new Node(poolName, machineId, poolId, poolVersion),
                // task state is running
                new Task(jobId, taskId, TaskState.Running, Os.Linux,
                    new TaskConfig(jobId, null, new TaskDetails(TaskType.Coverage, 100))));

        var func = new AgentEvents(Logger, ctx);

        var data = new NodeStateEnvelope(
            machineId,
            new WorkerEvent(Done: new WorkerDoneEvent(
                TaskId: taskId,
                ExitStatus: new ExitStatus(Code: 0, Signal: 0, Success: true),
                "stderr",
                "stdout")));

        var result = await func.Run(TestHttpRequestData.FromJson("POST", data));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var task = await ctx.TaskOperations.ListAll().SingleAsync();

        // should have transitioned into stopping
        Assert.Equal(TaskState.Stopping, task.State);
    }

    [Fact]
    public async Async.Task WorkerDone_WithFailedResult_ForRunningTask_MarksTaskAsStoppingAndErrored() {
        var ctx = CreateTestContext();

        await ctx.InsertAll(
                new Node(poolName, machineId, poolId, poolVersion),
                // task state is running
                new Task(jobId, taskId, TaskState.Running, Os.Linux,
                    new TaskConfig(jobId, null, new TaskDetails(TaskType.Coverage, 100))));


        var func = new AgentEvents(Logger, ctx);

        var data = new NodeStateEnvelope(
            machineId,
            new WorkerEvent(Done: new WorkerDoneEvent(
                TaskId: taskId,
                ExitStatus: new ExitStatus(Code: 0, Signal: 0, Success: false), // unsuccessful result
                "stderr",
                "stdout")));

        var result = await func.Run(TestHttpRequestData.FromJson("POST", data));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var task = await ctx.TaskOperations.ListAll().SingleAsync();
        Assert.Equal(TaskState.Stopping, task.State); // should have transitioned into stopping
        Assert.Equal(ErrorCode.TASK_FAILED, task.Error?.Code); // should be an error
    }

    [Fact]
    public async Async.Task WorkerDone_ForNonStartedTask_MarksTaskAsFailed() {
        var ctx = CreateTestContext();

        await ctx.InsertAll(
            new Node(poolName, machineId, poolId, poolVersion),
            // task state is scheduled, not running
            new Task(jobId, taskId, TaskState.Scheduled, Os.Linux,
                new TaskConfig(jobId, null, new TaskDetails(TaskType.Coverage, 100))));

        var func = new AgentEvents(Logger, ctx);

        var data = new NodeStateEnvelope(
            machineId,
            new WorkerEvent(Done: new WorkerDoneEvent(
                TaskId: taskId,
                ExitStatus: new ExitStatus(0, 0, true),
                "stderr",
                "stdout")));

        var result = await func.Run(TestHttpRequestData.FromJson("POST", data));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var task = await ctx.TaskOperations.ListAll().SingleAsync();

        // should be failed - it never started running
        Assert.Equal(TaskState.Stopping, task.State);
        Assert.Equal(ErrorCode.TASK_FAILED, task.Error?.Code);
    }
}
