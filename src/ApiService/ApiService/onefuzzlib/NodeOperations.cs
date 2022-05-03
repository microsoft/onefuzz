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

    Async.Task ReimageLongLivedNodes(Guid scaleSetId);

    Async.Task<Node> Create(
        Guid poolId,
        string poolName,
        Guid machineId,
        Guid? scaleSetId,
        string version,
        bool isNew = false);

    Async.Task SetHalt(Node node);

    IAsyncEnumerable<Node> GetDeadNodes(Guid scaleSetId, TimeSpan expirationPeriod);

    Async.Task MarkTasksStoppedEarly(Node node, Error? error = null);

    Async.Task ToReimage(Node node, bool done = false);

    static TimeSpan NODE_EXPIRATION_TIME = TimeSpan.FromHours(1.0);
    static TimeSpan NODE_REIMAGE_TIME = TimeSpan.FromDays(6.0);
}


/// Future work:
///
/// Enabling autoscaling for the scalesets based on the pool work queues.
/// https://docs.microsoft.com/en-us/azure/azure-monitor/platform/autoscale-common-metrics#commonly-used-storage-metrics

public class NodeOperations : StatefulOrm<Node, NodeState>, INodeOperations {

    private readonly INodeTasksOperations _nodeTasksOps;
    private readonly ITaskOperations _taskOps;
    private readonly INodeMessageOperations _nodeMessageOps;
    private readonly IEvents _events;
    private readonly ILogTracer _log;
    private readonly ICreds _creds;

    public NodeOperations(
        IStorage storage,
        ILogTracer log,
        IServiceConfig config,
        ITaskOperations taskOps,
        INodeTasksOperations nodeTasksOps,
        INodeMessageOperations nodeMessageOps,
        IEvents events,
        ICreds creds
        )
        : base(storage, log, config) {

        _taskOps = taskOps;
        _nodeTasksOps = nodeTasksOps;
        _nodeMessageOps = nodeMessageOps;
        _events = events;
        _log = log;
        _creds = creds;
    }


    /// Mark any excessively long lived node to be re-imaged.
    /// This helps keep nodes on scalesets that use `latest` OS image SKUs
    /// reasonably up-to-date with OS patches without disrupting running
    /// fuzzing tasks with patch reboot cycles.
    public async Async.Task ReimageLongLivedNodes(Guid scaleSetId) {
        var timeFilter = $"not (initialized_at ge datetime'{(DateTimeOffset.UtcNow - INodeOperations.NODE_REIMAGE_TIME).ToString("o")}')";

        await foreach (var node in QueryAsync($"(scaleset_id eq {scaleSetId}) and {timeFilter}")) {
            if (node.DebugKeepNode) {
                _log.Info($"removing debug_keep_node for expired node. scaleset_id:{node.ScalesetId} machine_id:{node.MachineId}");
            }
            await ToReimage(node with { DebugKeepNode = false });
        }
    }

    public async Async.Task ToReimage(Node node, bool done = false) {

        var nodeState = node.State;
        if (done) {
            if (!NodeStateHelper.ReadyForReset.Contains(node.State)) {
                nodeState = NodeState.Done;
            }
        }

        var reimageRequested = node.ReimageRequested;
        if (!node.ReimageRequested && !node.DeleteRequested) {
            _log.Info($"setting reimage_requested: {node.MachineId}");
            reimageRequested = true;
        }

        var updatedNode = node with { State = nodeState, ReimageRequested = reimageRequested };
        //if we're going to reimage, make sure the node doesn't pick up new work too.
        await SendStopIfFree(updatedNode);

        var r = await Replace(updatedNode);
        if (!r.IsOk) {
            _log.WithHttpStatus(r.ErrorV).Error("Failed to save Node record");
        }
    }

    public IAsyncEnumerable<Node> GetDeadNodes(Guid scaleSetId, TimeSpan expirationPeriod) {
        var minDate = DateTimeOffset.UtcNow - expirationPeriod;

        var filter = $"heartbeat lt datetime'{minDate.ToString("o")}' or Timestamp lt datetime'{minDate.ToString("o")}'";
        return QueryAsync(Query.And(filter, $"scaleset_id eq ${scaleSetId}"));
    }


    public async Async.Task<Node> Create(
        Guid poolId,
        string poolName,
        Guid machineId,
        Guid? scaleSetId,
        string version,
        bool isNew = false) {

        var node = new Node(poolName, machineId, poolId, version, ScalesetId: scaleSetId);

        ResultVoid<(int, string)> r;
        if (isNew) {
            r = await Replace(node);
        } else {
            r = await Update(node);
        }
        if (!r.IsOk) {
            _log.WithHttpStatus(r.ErrorV).Error($"failed to save NodeRecord, isNew: {isNew}");
        } else {
            await _events.SendEvent(
                new EventNodeCreated(
                    node.MachineId,
                    node.ScalesetId,
                    node.PoolName
                    )
                );
        }

        return node;
    }

    public async Async.Task Stop(Node node, bool done = false) {
        await ToReimage(node, done);
        await SendMessage(node, new NodeCommand(Stop: new StopNodeCommand()));
    }

    /// <summary>
    ///  Tell node to stop everything
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    public async Async.Task SetHalt(Node node) {
        _log.Info($"setting halt: {node.MachineId}");
        var updatedNode = node with { DeleteRequested = true };
        await Stop(updatedNode, true);
        await SendStopIfFree(updatedNode);
    }

    public async Async.Task SendStopIfFree(Node node) {
        var ver = new Version(_config.OneFuzzVersion.Split('-')[0]);
        if (ver >= Version.Parse("2.16.1")) {
            await SendMessage(node, new NodeCommand(StopIfFree: new NodeCommandStopIfFree()));
        }
    }

    public async Async.Task SendMessage(Node node, NodeCommand message) {
        var r = await _nodeMessageOps.Replace(new NodeMessage(node.MachineId, message));
        if (!r.IsOk) {
            _log.WithHttpStatus(r.ErrorV).Error($"failed to replace NodeMessge record for machine_id: {node.MachineId}");
        }
    }


    public async Async.Task<Node?> GetByMachineId(Guid machineId) {
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
            var q = Query.EqualAnyEnum("state", states);
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
                _log.WithHttpStatus(res.ErrorV).Error($"failed to delete node task entry for machine_id: {entry.MachineId}");
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
) : EntityBase {
    public NodeMessage(Guid machineId, NodeCommand message) : this(machineId, NewSortedKey, message) { }
};

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
                _log.WithHttpStatus(r.ErrorV).Error($"failed to delete message for node {machineId}");
            }
        }
    }
}
