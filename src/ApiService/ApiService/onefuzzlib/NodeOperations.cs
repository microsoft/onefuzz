using System.Text.Json;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface INodeOperations : IStatefulOrm<Node, NodeState> {
    Task<Node?> GetByMachineId(Guid machineId);

    Task<bool> CanProcessNewWork(Node node);

    Task<OneFuzzResultVoid> AcquireScaleInProtection(Node node);

    bool IsOutdated(Node node);
    Async.Task Stop(Node node, bool done = false);
    bool IsTooOld(Node node);
    bool CouldShrinkScaleset(Node node);
    Async.Task SetHalt(Node node);
    Async.Task SetState(Node node, NodeState state);
    Async.Task ToReimage(Node node, bool done = false);
    void SendStopIfFree(Node node);
    IAsyncEnumerable<Node> SearchStates(Guid? poolId = default,
        Guid? scaleSetId = default,
        IList<NodeState>? states = default,
        string? poolName = default,
        bool excludeUpdateScheduled = false,
        int? numResults = default);

    new Async.Task Delete(Node node);
}

public class NodeOperations : StatefulOrm<Node, NodeState>, INodeOperations {

    // 1 hour
    private static readonly TimeSpan NODE_EXPIRATION_TIME = new TimeSpan(1, 0, 0);

    // 6 days
    private static readonly TimeSpan NODE_REIMAGE_TIME = new TimeSpan(6, 0, 0, 0);
    private IScalesetOperations _scalesetOperations;
    private IPoolOperations _poolOperations;
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
        IEvents events,
        IScalesetOperations scalesetOperations,
        IPoolOperations poolOperations
        )
        : base(storage, log, config) {

        _taskOps = taskOps;
        _nodeTasksOps = nodeTasksOps;
        _nodeMessageOps = nodeMessageOps;
        _events = events;
        _scalesetOperations = scalesetOperations;
        _poolOperations = poolOperations;
    }

    public Task<OneFuzzResultVoid> AcquireScaleInProtection(Node node) {
        throw new NotImplementedException();
    }

    public async Async.Task<bool> ScalesetNodeExists(Node node) {
        if (node.ScalesetId == null) {
            return false;
        }

        var scalesetResult = await _scalesetOperations.GetById(node.ScalesetId!);
        if (!scalesetResult.IsOk || scalesetResult.OkV == null) {
            return false;
        }
    }

    public async Task<bool> CanProcessNewWork(Node node) {
        if (IsOutdated(node)) {
            _logTracer.Info($"can_process_new_work agent and service versions differ, stopping node. machine_id:{node.MachineId} agent_version:{node.Version} service_version:{_config.OneFuzzVersion}");
            await Stop(node, done: true);
            return false;
        }

        if (IsTooOld(node)) {
            _logTracer.Info($"can_process_new_work node is too old. machine_id:{node.MachineId}");
            await Stop(node, done: true);
            return false;
        }

        if (!NodeStateHelper.CanProcessNewWork.Contains(node.State)) {
            _logTracer.Info($"can_process_new_work node not in appropriate state for new work machine_id:{node.MachineId} state:{node.State}");
            return false;
        }

        if (NodeStateHelper.ReadyForReset.Contains(node.State)) {
            _logTracer.Info($"can_process_new_work node is set for reset. machine_id:{node.MachineId}");
            return false;
        }

        if (node.DeleteRequested) {
            _logTracer.Info($"can_process_new_work is set to be deleted. machine_id:{node.MachineId}");
            await Stop(node, done: true);
            return false;
        }

        if (node.ReimageRequested) {
            _logTracer.Info($"can_process_new_work is set to be reimaged. machine_id:{node.MachineId}");
            await Stop(node, done: true);
            return false;
        }

        if (CouldShrinkScaleset(node)) {
            _logTracer.Info($"can_process_new_work node scheduled to shrink. machine_id:{node.MachineId}");
            await SetHalt(node);
            return false;
        }

        if (node.ScalesetId != null) {
            var scalesetResult = await _scalesetOperations.GetById(node.ScalesetId.Value);
            if (!scalesetResult.IsOk || scalesetResult.OkV == null) {
                _logTracer.Info($"can_process_new_work invalid scaleset. scaleset_id:{node.ScalesetId} machine_id:{node.MachineId}");
                return false;
            }
            var scaleset = scalesetResult.OkV!;

            if (!ScalesetStateHelper.Available().Contains(scaleset.State)) {
                _logTracer.Info($"can_process_new_work scaleset not available for work. scaleset_id:{node.ScalesetId} machine_id:{node.MachineId}");
                return false;
            }
        }

        var poolResult = await _poolOperations.GetByName(node.PoolName);
        if (!poolResult.IsOk || poolResult.OkV == null) {
            _logTracer.Info($"can_schedule - invalid pool. pool_name:{node.PoolName} machine_id:{node.MachineId}");
            return false;
        }

        var pool = poolResult.OkV!;
        if (!PoolStateHelper.Available().Contains(pool.State)) {
            _logTracer.Info($"can_schedule - pool is not available for work. pool_name:{node.PoolName} machine_id:{node.MachineId}");
            return false;
        }

        return true;
    }

    public async Task<Node?> GetByMachineId(Guid machineId) {
        var data = QueryAsync(filter: $"RowKey eq '{machineId}'");

        return await data.FirstOrDefaultAsync();
    }

    public bool IsOutdated(Node node) {
        return node.Version != _config.OneFuzzVersion;
    }

    public async Async.Task Stop(Node node, bool done = false) {
        await ToReimage(node, done);
        await SendMessage(node, new NodeCommand(new StopNodeCommand(), null, null, null));
    }

    public bool IsTooOld(Node node) {
        return node.ScalesetId != null
            && node.InitializedAt != null
            && node.InitializedAt < DateTime.UtcNow - NODE_REIMAGE_TIME;
    }

    public bool CouldShrinkScaleset(Node node) {
        throw new NotImplementedException();
    }

    /// Tell the node to stop everything.
    public async Async.Task SetHalt(Node node) {
        _logTracer.Info($"setting halt: {node.MachineId}");

        var newNode = node with { DeleteRequested = true };
        await Stop(newNode);
        await SetState(node, NodeState.Halt);
    }

    public async Async.Task SetState(Node node, NodeState state) {
        var newNode = node;
        if (node.State != state) {
            newNode = newNode with { State = state };
            await _events.SendEvent(new EventNodeStateUpdated(
                node.MachineId,
                node.ScalesetId,
                node.PoolName,
                node.State
            ));
        }

        await Replace(newNode);
    }

    public async Async.Task ToReimage(Node node, bool done = false) {
        var newNode = node;
        if (done && !NodeStateHelper.ReadyForReset.Contains(node.State)) {
            newNode = newNode with { State = NodeState.Done };
        }

        if (!node.ReimageRequested && !node.DeleteRequested) {
            _logTracer.Info($"setting reimage_requested: {node.MachineId}");
            newNode = newNode with { ReimageRequested = true };
        }

        SendStopIfFree(node);
        await Replace(newNode);
    }

    public void SendStopIfFree(Node node) {
        throw new NotImplementedException();
    }

    private async Async.Task SendMessage(Node node, NodeCommand message) {
        await _nodeMessageOps.SendMessage(node.MachineId, message);
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

    Async.Task SendMessage(Guid machineId, NodeCommand message, string? messageId = null);
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

    public async Async.Task SendMessage(Guid machineId, NodeCommand message, string? messageId = null) {
        messageId = messageId ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        await Insert(new NodeMessage(machineId, messageId, message));
    }
}
