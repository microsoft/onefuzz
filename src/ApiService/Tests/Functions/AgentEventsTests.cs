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
        var func = new AgentEvents(Logger, Context);

        var data = new NodeStateEnvelope(
            MachineId: Guid.NewGuid(),
            Event: new WorkerEvent(null, null));

        var result = await func.Run(TestHttpRequestData.FromJson("POST", data));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }


    [Fact]
    public async Async.Task WorkerDone_WithSuccessfulResult_ForRunningTask_MarksTaskAsStopping() {
        await Context.InsertAll(
                new Node(poolName, machineId, poolId, poolVersion),
                // task state is running
                new Task(jobId, taskId, TaskState.Running, Os.Linux,
                    new TaskConfig(jobId, null, new TaskDetails(TaskType.Coverage, 100))));

        var func = new AgentEvents(Logger, Context);

        var data = new NodeStateEnvelope(
            MachineId: machineId,
            Event: new WorkerEvent(Done: new WorkerDoneEvent(
                TaskId: taskId,
                ExitStatus: new ExitStatus(Code: 0, Signal: 0, Success: true),
                "stderr",
                "stdout")));

        var result = await func.Run(TestHttpRequestData.FromJson("POST", data));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var task = await Context.TaskOperations.SearchAll().SingleAsync();

        // should have transitioned into stopping
        Assert.Equal(TaskState.Stopping, task.State);
    }

    [Fact]
    public async Async.Task WorkerDone_WithFailedResult_ForRunningTask_MarksTaskAsStoppingAndErrored() {
        await Context.InsertAll(
                new Node(poolName, machineId, poolId, poolVersion),
                // task state is running
                new Task(jobId, taskId, TaskState.Running, Os.Linux,
                    new TaskConfig(jobId, null, new TaskDetails(TaskType.Coverage, 100))));


        var func = new AgentEvents(Logger, Context);

        var data = new NodeStateEnvelope(
            MachineId: machineId,
            Event: new WorkerEvent(Done: new WorkerDoneEvent(
                TaskId: taskId,
                ExitStatus: new ExitStatus(Code: 0, Signal: 0, Success: false), // unsuccessful result
                "stderr",
                "stdout")));

        var result = await func.Run(TestHttpRequestData.FromJson("POST", data));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var task = await Context.TaskOperations.SearchAll().SingleAsync();
        Assert.Equal(TaskState.Stopping, task.State); // should have transitioned into stopping
        Assert.Equal(ErrorCode.TASK_FAILED, task.Error?.Code); // should be an error
    }

    [Fact]
    public async Async.Task WorkerDone_ForNonStartedTask_MarksTaskAsFailed() {
        await Context.InsertAll(
            new Node(poolName, machineId, poolId, poolVersion),
            // task state is scheduled, not running
            new Task(jobId, taskId, TaskState.Scheduled, Os.Linux,
                new TaskConfig(jobId, null, new TaskDetails(TaskType.Coverage, 100))));

        var func = new AgentEvents(Logger, Context);

        var data = new NodeStateEnvelope(
            MachineId: machineId,
            Event: new WorkerEvent(Done: new WorkerDoneEvent(
                TaskId: taskId,
                ExitStatus: new ExitStatus(0, 0, true),
                "stderr",
                "stdout")));

        var result = await func.Run(TestHttpRequestData.FromJson("POST", data));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var task = await Context.TaskOperations.SearchAll().SingleAsync();

        // should be failed - it never started running
        Assert.Equal(TaskState.Stopping, task.State);
        Assert.Equal(ErrorCode.TASK_FAILED, task.Error?.Code);
    }

    [Fact]
    public async Async.Task WorkerRunning_ForMissingTask_ReturnsError() {
        await Context.InsertAll(
            new Node(poolName, machineId, poolId, poolVersion));

        var func = new AgentEvents(Logger, Context);
        var data = new NodeStateEnvelope(
            MachineId: machineId,
            Event: new WorkerEvent(Running: new WorkerRunningEvent(taskId)));

        var result = await func.Run(TestHttpRequestData.FromJson("POST", data));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("unable to find task", BodyAsString(result));
    }

    [Fact]
    public async Async.Task WorkerRunning_ForMissingNode_ReturnsError() {
        await Context.InsertAll(
            new Task(jobId, taskId, TaskState.Running, Os.Linux,
                new TaskConfig(jobId, null, new TaskDetails(TaskType.Coverage, 0))));

        var func = new AgentEvents(Logger, Context);
        var data = new NodeStateEnvelope(
            MachineId: machineId,
            Event: new WorkerEvent(Running: new WorkerRunningEvent(taskId)));

        var result = await func.Run(TestHttpRequestData.FromJson("POST", data));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("unable to find node", BodyAsString(result));
    }

    [Fact]
    public async Async.Task WorkerRunning_HappyPath() {
        await Context.InsertAll(
            new Node(poolName, machineId, poolId, poolVersion),
            new Task(jobId, taskId, TaskState.Running, Os.Linux,
                new TaskConfig(jobId, null, new TaskDetails(TaskType.Coverage, 0))));

        var func = new AgentEvents(Logger, Context);
        var data = new NodeStateEnvelope(
            MachineId: machineId,
            Event: new WorkerEvent(Running: new WorkerRunningEvent(taskId)));

        var result = await func.Run(TestHttpRequestData.FromJson("POST", data));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // perform checks in parallel
        await Async.Task.WhenAll(
            Async.Task.Run(async () => {
                // task should be marked running
                var task = await Context.TaskOperations.SearchAll().SingleAsync();
                Assert.Equal(TaskState.Running, task.State);
            }),
            Async.Task.Run(async () => {
                // node should now be marked busy
                var node = await Context.NodeOperations.SearchAll().SingleAsync();
                Assert.Equal(NodeState.Busy, node.State);
            }),
            Async.Task.Run(async () => {
                // there should be a node-task with correct values
                var nodeTask = await Context.NodeTasksOperations.SearchAll().SingleAsync();
                Assert.Equal(machineId, nodeTask.MachineId);
                Assert.Equal(taskId, nodeTask.TaskId);
                Assert.Equal(NodeTaskState.Running, nodeTask.State);
            }),
            Async.Task.Run(async () => {
                // there should be a task-event with correct values
                var taskEvent = await Context.TaskEventOperations.SearchAll().SingleAsync();
                Assert.Equal(taskId, taskEvent.TaskId);
                Assert.Equal(machineId, taskEvent.MachineId);
                Assert.Equal(new WorkerEvent(Running: new WorkerRunningEvent(taskId)), taskEvent.EventData);
            }));
    }
}
