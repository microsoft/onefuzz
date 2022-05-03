using System.Text.Json;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface INodeOperations : IStatefulOrm<Node, NodeState> {
    Task<Node?> GetByMachineId(Guid machineId);

    IAsyncEnumerable<Node> SearchStates(Guid? poolId = default,
        Guid? scaleSetId = default,
        IList<NodeState>? states = default,
        string? poolName = default,
        bool excludeUpdateScheduled = false,
        int? numResults = default);

    new Async.Task Delete(Node node);
}

public class NodeOperations : StatefulOrm<Node, NodeState>, INodeOperations {

    private readonly INodeTasksOperations _nodeTasksOps;
    private readonly ITaskOperations _taskOps;
    private readonly INodeMessageOperations _nodeMessageOps;
    private readonly IEvents _events;

    public NodeOperations(
        IStorage storage,
        ILogTracer log,
        IServiceConfig config,
        ITaskOperations taskOps,
        INodeTasksOperations nodeTasksOps,
        INodeMessageOperations nodeMessageOps,
        IEvents events
        )
        : base(storage, log, config) {

        _taskOps = taskOps;
        _nodeTasksOps = nodeTasksOps;
        _nodeMessageOps = nodeMessageOps;
        _events = events;
    }

    public async Task<Node?> GetByMachineId(Guid machineId) {
        var data = QueryAsync(filter: $"RowKey eq '{machineId}'");

        return await data.FirstOrDefaultAsync();
    }

    public static string SearchStatesQuery(
        string oneFuzzVersion,
        Guid? poolId = default,
        Guid? scaleSetId = default,
        IEnumerable<NodeState>? states = default,
        string? poolName = default,
        bool excludeUpdateScheduled = false,
        int? numResults = default) {

        List<string> queryParts = new();

        if (poolId is not null) {
            queryParts.Add($"(pool_id eq '{poolId}')");
        }

        if (scaleSetId is not null) {
            queryParts.Add($"(scaleset_id eq '{scaleSetId}')");
        }

        if (states is not null) {
            IEnumerable<string> convertedStates = states.Select(x => JsonSerializer.Serialize(x, EntityConverter.GetJsonSerializerOptions()).Trim('"'));
            var q = Query.EqualAny("state", convertedStates);
            queryParts.Add($"({q})");
        }

        if (excludeUpdateScheduled) {
            queryParts.Add($"reimage_requested eq false");
            queryParts.Add($"delete_requested eq false");
        }

        //# azure table query always return false when the column does not exist
        //# We write the query this way to allow us to get the nodes where the
        //# version is not defined as well as the nodes with a mismatched version
        var versionQuery = $"not (version eq '{oneFuzzVersion}')";
        queryParts.Add(versionQuery);

        return Query.And(queryParts);
    }


    public IAsyncEnumerable<Node> SearchStates(
        Guid? poolId = default,
        Guid? scaleSetId = default,
        IList<NodeState>? states = default,
        string? poolName = default,
        bool excludeUpdateScheduled = false,
        int? numResults = default) {
        var query = NodeOperations.SearchStatesQuery(_config.OneFuzzVersion, poolId, scaleSetId, states, poolName, excludeUpdateScheduled, numResults);
        return QueryAsync(query);
    }

    public async Async.Task MarkTasksStoppedEarly(Node node, Error? error = null) {
        if (error is null) {
            error = new Error(ErrorCode.TASK_FAILED, new[] { $"node reimaged during task execution.  machine_id: {node.MachineId}" });
        }

        await foreach (var entry in _nodeTasksOps.GetByMachineId(node.MachineId)) {
            var task = await _taskOps.GetByTaskId(entry.TaskId);
            if (task is not null) {
                await _taskOps.MarkFailed(task, error);
            }
            if (!node.DebugKeepNode) {
                await Delete(node);
            }
        }
    }

    public new async Async.Task Delete(Node node) {
        await MarkTasksStoppedEarly(node);
        await _nodeTasksOps.ClearByMachineId(node.MachineId);
        await _nodeMessageOps.ClearMessages(node.MachineId);
        await base.Delete(node);

        await _events.SendEvent(new EventNodeDeleted(node.MachineId, node.ScalesetId, node.PoolName));
    }
}


public interface INodeTasksOperations : IStatefulOrm<NodeTasks, NodeTaskState> {
    IAsyncEnumerable<Node> GetNodesByTaskId(Guid taskId, INodeOperations nodeOps);
    IAsyncEnumerable<NodeAssignment> GetNodeAssignments(Guid taskId, INodeOperations nodeOps);
    IAsyncEnumerable<NodeTasks> GetByMachineId(Guid machineId);
    IAsyncEnumerable<NodeTasks> GetByTaskId(Guid taskId);
    Async.Task ClearByMachineId(Guid machineId);
}

public class NodeTasksOperations : StatefulOrm<NodeTasks, NodeTaskState>, INodeTasksOperations {

    ILogTracer _log;

    public NodeTasksOperations(IStorage storage, ILogTracer log, IServiceConfig config)
        : base(storage, log, config) {
        _log = log;
    }

    //TODO: suggest by Cheick: this can probably be optimize by query all NodesTasks then query the all machine in single request
    public async IAsyncEnumerable<Node> GetNodesByTaskId(Guid taskId, INodeOperations nodeOps) {
        List<Node> results = new();
        await foreach (var entry in QueryAsync($"task_id eq '{taskId}'")) {
            var node = await nodeOps.GetByMachineId(entry.MachineId);
            if (node is not null) {
                yield return node;
            }
        }
    }
    public async IAsyncEnumerable<NodeAssignment> GetNodeAssignments(Guid taskId, INodeOperations nodeOps) {

        await foreach (var entry in QueryAsync($"task_id eq '{taskId}'")) {
            var node = await nodeOps.GetByMachineId(entry.MachineId);
            if (node is not null) {
                var nodeAssignment = new NodeAssignment(node.MachineId, node.ScalesetId, entry.State);
                yield return nodeAssignment;
            }
        }
    }

    public IAsyncEnumerable<NodeTasks> GetByMachineId(Guid machineId) {
        return QueryAsync($"macine_id eq '{machineId}'");
    }

    public IAsyncEnumerable<NodeTasks> GetByTaskId(Guid taskId) {
        return QueryAsync($"task_id eq '{taskId}'");
    }

    public async Async.Task ClearByMachineId(Guid machineId) {
        _log.Info($"clearing tasks for node {machineId}");
        await foreach (var entry in GetByMachineId(machineId)) {
            var res = await Delete(entry);
            if (!res.IsOk) {
                _log.Error($"failed to delete node task entry for machine_id: {entry.MachineId} due to [{res.ErrorV.Item1}] {res.ErrorV.Item2}");
            }
        }
    }
}

//# this isn't anticipated to be needed by the client, hence it not
//# being in onefuzztypes
public record NodeMessage(
    [PartitionKey] Guid MachineId,
    [RowKey] string MessageId,
    NodeCommand Message
) : EntityBase;

public interface INodeMessageOperations : IOrm<NodeMessage> {
    IAsyncEnumerable<NodeMessage> GetMessage(Guid machineId);
    Async.Task ClearMessages(Guid machineId);
}


public class NodeMessageOperations : Orm<NodeMessage>, INodeMessageOperations {

    private readonly ILogTracer _log;
    public NodeMessageOperations(IStorage storage, ILogTracer log, IServiceConfig config) : base(storage, log, config) {
        _log = log;
    }

    public IAsyncEnumerable<NodeMessage> GetMessage(Guid machineId) {
        return QueryAsync($"machine_id eq '{machineId}'");
    }

    public async Async.Task ClearMessages(Guid machineId) {
        _log.Info($"clearing messages for node {machineId}");

        await foreach (var message in GetMessage(machineId)) {
            var r = await Delete(message);
            if (!r.IsOk) {
                _log.Error($"failed to delete message for node {machineId} due to [{r.ErrorV.Item1}] {r.ErrorV.Item2}");
            }
        }
    }
}
