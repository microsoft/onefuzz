﻿using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure.Data.Tables;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface INodeOperations : IStatefulOrm<Node, NodeState> {
    Task<Node?> GetByMachineId(Guid machineId);

    Task<bool> CanProcessNewWork(Node node);

    Task<OneFuzzResultVoid> AcquireScaleInProtection(Node node);

    bool IsOutdated(Node node);
    Async.Task Stop(Node node, bool done = false);
    bool IsTooOld(Node node);
    Task<bool> CouldShrinkScaleset(Node node);
    Async.Task SetHalt(Node node);
    Async.Task SetState(Node node, NodeState state);
    Async.Task ToReimage(Node node, bool done = false);
    Async.Task SendStopIfFree(Node node);
    IAsyncEnumerable<Node> SearchStates(Guid? poolId = default,
        Guid? scalesetId = default,
        IEnumerable<NodeState>? states = default,
        PoolName? poolName = default,
        bool excludeUpdateScheduled = false,
        int? numResults = default);

    new Async.Task Delete(Node node);

    Async.Task ReimageLongLivedNodes(Guid scaleSetId);

    Async.Task<Node> Create(
        Guid poolId,
        PoolName poolName,
        Guid machineId,
        Guid? scaleSetId,
        string version,
        bool isNew = false);

    IAsyncEnumerable<Node> GetDeadNodes(Guid scaleSetId, TimeSpan expirationPeriod);

    Async.Task MarkTasksStoppedEarly(Node node, Error? error = null);
    static readonly TimeSpan NODE_EXPIRATION_TIME = TimeSpan.FromHours(1.0);
    static readonly TimeSpan NODE_REIMAGE_TIME = TimeSpan.FromDays(6.0);

    Async.Task StopTask(Guid task_id);

    Async.Task<OneFuzzResult<bool>> AddSshPublicKey(Node node, string publicKey);

    Async.Task MarkOutdatedNodes();
    Async.Task CleanupBusyNodesWithoutWork();

    IAsyncEnumerable<Node> SearchByPoolName(PoolName poolName);

    Async.Task SetShutdown(Node node);
}


/// Future work:
///
/// Enabling autoscaling for the scalesets based on the pool work queues.
/// https://docs.microsoft.com/en-us/azure/azure-monitor/platform/autoscale-common-metrics#commonly-used-storage-metrics

public class NodeOperations : StatefulOrm<Node, NodeState, NodeOperations>, INodeOperations {


    public NodeOperations(
        ILogTracer log,
        IOnefuzzContext context
        )
        : base(log, context) {

    }

    public async Task<OneFuzzResultVoid> AcquireScaleInProtection(Node node) {
        if (await ScalesetNodeExists(node) && node.ScalesetId is Guid scalesetId) {
            _logTracer.Info($"Setting scale-in protection on node {node.MachineId}");
            return await _context.VmssOperations.UpdateScaleInProtection(scalesetId, node.MachineId, protectFromScaleIn: true);
        }

        return OneFuzzResultVoid.Ok;
    }

    public async Async.Task<bool> ScalesetNodeExists(Node node) {
        if (node.ScalesetId == null) {
            return false;
        }

        var scalesetResult = await _context.ScalesetOperations.GetById((Guid)(node.ScalesetId!));
        if (!scalesetResult.IsOk || scalesetResult.OkV == null) {
            return false;
        }
        var scaleset = scalesetResult.OkV;

        var instanceId = await _context.VmssOperations.GetInstanceId(scaleset.ScalesetId, node.MachineId);
        return instanceId.IsOk;
    }

    public async Task<bool> CanProcessNewWork(Node node) {
        if (IsOutdated(node) && _context.ServiceConfiguration.OneFuzzAllowOutdatedAgent != "true") {
            _logTracer.Info($"can_process_new_work agent and service versions differ, stopping node. machine_id:{node.MachineId} agent_version:{node.Version} service_version:{_context.ServiceConfiguration.OneFuzzVersion}");
            await Stop(node, done: true);
            return false;
        }

        if (IsTooOld(node)) {
            _logTracer.Info($"can_process_new_work node is too old. machine_id:{node.MachineId}");
            await Stop(node, done: true);
            return false;
        }

        if (!node.State.CanProcessNewWork()) {
            _logTracer.Info($"can_process_new_work node not in appropriate state for new work machine_id:{node.MachineId} state:{node.State}");
            return false;
        }

        if (node.State.ReadyForReset()) {
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

        if (await CouldShrinkScaleset(node)) {
            _logTracer.Info($"can_process_new_work node scheduled to shrink. machine_id:{node.MachineId}");
            await SetHalt(node);
            return false;
        }

        if (node.ScalesetId != null) {
            var scalesetResult = await _context.ScalesetOperations.GetById(node.ScalesetId.Value);
            if (!scalesetResult.IsOk) {
                _logTracer.Info($"can_process_new_work invalid scaleset. scaleset_id:{node.ScalesetId} machine_id:{node.MachineId}");
                return false;
            }

            var scaleset = scalesetResult.OkV;
            if (!scaleset.State.IsAvailable()) {
                _logTracer.Info($"can_process_new_work scaleset not available for work. scaleset_id:{node.ScalesetId} machine_id:{node.MachineId}");
                return false;
            }
        }

        var poolResult = await _context.PoolOperations.GetByName(node.PoolName);
        if (!poolResult.IsOk) {
            _logTracer.Info($"can_schedule - invalid pool. pool_name:{node.PoolName} machine_id:{node.MachineId}");
            return false;
        }

        var pool = poolResult.OkV;
        if (!PoolStateHelper.Available.Contains(pool.State)) {
            _logTracer.Info($"can_schedule - pool is not available for work. pool_name:{node.PoolName} machine_id:{node.MachineId}");
            return false;
        }

        return true;
    }


    /// Mark any excessively long lived node to be re-imaged.
    /// This helps keep nodes on scalesets that use `latest` OS image SKUs
    /// reasonably up-to-date with OS patches without disrupting running
    /// fuzzing tasks with patch reboot cycles.
    public async Async.Task ReimageLongLivedNodes(Guid scaleSetId) {
        var timeFilter = $"not (initialized_at ge datetime'{(DateTimeOffset.UtcNow - INodeOperations.NODE_REIMAGE_TIME).ToString("o")}')";

        await foreach (var node in QueryAsync($"(scaleset_id eq '{scaleSetId}') and {timeFilter}")) {
            if (node.DebugKeepNode) {
                _logTracer.Info($"removing debug_keep_node for expired node. scaleset_id:{node.ScalesetId} machine_id:{node.MachineId}");
            }
            await ToReimage(node with { DebugKeepNode = false });
        }
    }


    public async Async.Task MarkOutdatedNodes() {
        //#if outdated agents are allowed, do not attempt to update
        bool allowOutdatedAgent;
        var parsed = bool.TryParse(_context.ServiceConfiguration.OneFuzzAllowOutdatedAgent ?? "false", out allowOutdatedAgent);
        if (parsed && allowOutdatedAgent) {
            return;
        }

        var outdated = this.SearchOutdated(excludeUpdateScheduled: true);
        await foreach (var node in outdated) {
            _logTracer.Info($"node is outdated: {node.MachineId} - node_version:{node.Version} api_version:");

            if (node.Version == "1.0.0") {
                await ToReimage(node, done: true);
            } else {
                await ToReimage(node);
            }
        }
    }


    public static string SearchOutdatedQuery(
    string oneFuzzVersion,
    Guid? poolId = null,
    Guid? scalesetId = null,
    IEnumerable<NodeState>? states = null,
    PoolName? poolName = null,
    bool excludeUpdateScheduled = false,
    int? numResults = null) {

        List<string> queryParts = new();

        if (poolId is not null) {
            queryParts.Add($"(pool_id eq '{poolId}')");
        }

        if (poolName is not null) {
            queryParts.Add($"(pool_name eq '{poolName}')");
        }

        if (scalesetId is not null) {
            queryParts.Add($"(scaleset_id eq '{scalesetId}')");
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
        var versionQuery = TableClient.CreateQueryFilter($"not (version eq {oneFuzzVersion})");
        queryParts.Add(versionQuery);
        return Query.And(queryParts);
    }

    IAsyncEnumerable<Node> SearchOutdated(
            Guid? poolId = null,
            Guid? scalesetId = null,
            IEnumerable<NodeState>? states = null,
            PoolName? poolName = null,
            bool excludeUpdateScheduled = false,
            int? numResults = null) {

        var query = SearchOutdatedQuery(_context.ServiceConfiguration.OneFuzzVersion, poolId, scalesetId, states, poolName, excludeUpdateScheduled, numResults);
        if (numResults is null) {
            return QueryAsync(query);
        } else {
            return QueryAsync(query).Take(numResults.Value!);
        }
    }

    public async Async.Task CleanupBusyNodesWithoutWork() {
        //# There is a potential race condition if multiple `Node.stop_task` calls
        //# are made concurrently.  By performing this check regularly, any nodes
        //# that hit this race condition will get cleaned up.
        var nodes = _context.NodeOperations.SearchStates(states: NodeStateHelper.BusyStates);

        await foreach (var node in nodes) {
            await StopIfComplete(node, true);
        }
    }

    public async Async.Task ToReimage(Node node, bool done = false) {

        var nodeState = node.State;
        if (done) {
            if (!node.State.ReadyForReset()) {
                nodeState = NodeState.Done;
            }
        }

        var reimageRequested = node.ReimageRequested;
        if (!node.ReimageRequested && !node.DeleteRequested) {
            _logTracer.Info($"setting reimage_requested: {node.MachineId}");
            reimageRequested = true;
        }

        var updatedNode = node with { State = nodeState, ReimageRequested = reimageRequested };
        //if we're going to reimage, make sure the node doesn't pick up new work too.
        await SendStopIfFree(updatedNode);

        var r = await Replace(updatedNode);
        if (!r.IsOk) {
            _logTracer.WithHttpStatus(r.ErrorV).Error("Failed to save Node record");
        }
    }

    public IAsyncEnumerable<Node> GetDeadNodes(Guid scaleSetId, TimeSpan expirationPeriod) {
        var minDate = DateTimeOffset.UtcNow - expirationPeriod;

        var filter = $"heartbeat lt datetime'{minDate.ToString("o")}' or Timestamp lt datetime'{minDate.ToString("o")}'";
        var query = Query.And(filter, $"scaleset_id eq '{scaleSetId}'");
        return QueryAsync(query);
    }


    public async Async.Task<Node> Create(
        Guid poolId,
        PoolName poolName,
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
            _logTracer.WithHttpStatus(r.ErrorV).Error($"failed to save NodeRecord, isNew: {isNew}");
        } else {
            await _context.Events.SendEvent(
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
        _logTracer.Info($"setting halt: {node.MachineId}");
        var updatedNode = node with { DeleteRequested = true };
        await Stop(updatedNode, true);
        await SendStopIfFree(updatedNode);
    }

    public async Async.Task SetShutdown(Node node) {
        //don't give out more work to the node, but let it finish existing work
        _logTracer.Info($"setting delete_requested: {node.MachineId}");
        node = node with { DeleteRequested = true };
        var r = await Replace(node);
        if (!r.IsOk) {
            _logTracer.Error($"failed to update node with delete requested. machine id: {node.MachineId}, pool name: {node.PoolName}, pool id: {node.PoolId}, scaleset id: {node.ScalesetId}");
        }

        await SendStopIfFree(node);
    }


    public async Async.Task SendStopIfFree(Node node) {
        var ver = new Version(_context.ServiceConfiguration.OneFuzzVersion.Split('-')[0]);
        if (ver >= Version.Parse("2.16.1")) {
            await SendMessage(node, new NodeCommand(StopIfFree: new NodeCommandStopIfFree()));
        }
    }

    public async Async.Task SendMessage(Node node, NodeCommand message) {
        var r = await _context.NodeMessageOperations.Replace(new NodeMessage(node.MachineId, message));
        if (!r.IsOk) {
            _logTracer.WithHttpStatus(r.ErrorV).Error($"failed to replace NodeMessge record for machine_id: {node.MachineId}");
        }
    }


    public async Async.Task<Node?> GetByMachineId(Guid machineId) {
        var data = QueryAsync(filter: Query.RowKey(machineId.ToString()));

        return await data.FirstOrDefaultAsync();
    }

    public bool IsOutdated(Node node) {
        return node.Version != _context.ServiceConfiguration.OneFuzzVersion;
    }

    public bool IsTooOld(Node node) {
        return node.ScalesetId != null
            && node.InitializedAt != null
            && node.InitializedAt < DateTime.UtcNow - INodeOperations.NODE_REIMAGE_TIME;
    }

    public async Task<bool> CouldShrinkScaleset(Node node) {
        if (node.ScalesetId is Guid scalesetId) {
            var queue = new ShrinkQueue(scalesetId, _context.Queue, _logTracer);
            if (await queue.ShouldShrink()) {
                return true;
            }
        }

        if (node.PoolId is Guid poolId) {
            var queue = new ShrinkQueue(poolId, _context.Queue, _logTracer);
            if (await queue.ShouldShrink()) {
                return true;
            }
        }

        return false;
    }

    public async Async.Task SetState(Node node, NodeState state) {
        var newNode = node;
        if (node.State != state) {
            newNode = newNode with { State = state };
            await _context.Events.SendEvent(new EventNodeStateUpdated(
                node.MachineId,
                node.ScalesetId,
                node.PoolName,
                node.State
            ));
        }

        var r = await Replace(newNode);
        if (!r.IsOk) {
            _logTracer.Error($"Failed to update node for machine: {newNode.MachineId} to state {state} due to {r.ErrorV}");
        }
    }

    public static string SearchStatesQuery(
        string oneFuzzVersion,
        Guid? poolId = default,
        Guid? scaleSetId = default,
        IEnumerable<NodeState>? states = default,
        PoolName? poolName = default,
        bool excludeUpdateScheduled = false,
        int? numResults = default) {

        List<string> queryParts = new();

        if (poolId is not null) {
            queryParts.Add($"(pool_id eq '{poolId}')");
        }

        if (poolName is not null) {
            queryParts.Add($"(PartitionKey eq '{poolName}')");
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
        var versionQuery = TableClient.CreateQueryFilter($"not (version eq {oneFuzzVersion})");
        queryParts.Add(versionQuery);

        return Query.And(queryParts);
    }


    public IAsyncEnumerable<Node> SearchStates(
        Guid? poolId = default,
        Guid? scalesetId = default,
        IEnumerable<NodeState>? states = default,
        PoolName? poolName = default,
        bool excludeUpdateScheduled = false,
        int? numResults = default) {
        var query = NodeOperations.SearchStatesQuery(_context.ServiceConfiguration.OneFuzzVersion, poolId, scalesetId, states, poolName, excludeUpdateScheduled, numResults);

        if (numResults is null) {
            return QueryAsync(query);
        } else {
            return QueryAsync(query).TakeWhile((_, i) => i < numResults);
        }
    }

    public IAsyncEnumerable<Node> SearchByPoolName(PoolName poolName) {
        return QueryAsync(TableClient.CreateQueryFilter($"(pool_name eq {poolName})"));
    }


    public async Async.Task MarkTasksStoppedEarly(Node node, Error? error = null) {
        if (error is null) {
            error = new Error(ErrorCode.TASK_FAILED, new[] { $"node reimaged during task execution.  machine_id: {node.MachineId}" });
        }

        await foreach (var entry in _context.NodeTasksOperations.GetByMachineId(node.MachineId)) {
            var task = await _context.TaskOperations.GetByTaskId(entry.TaskId);
            if (task is not null) {
                await _context.TaskOperations.MarkFailed(task, error);
            }
            if (!node.DebugKeepNode) {
                await Delete(node);
            }
        }
    }

    public new async Async.Task Delete(Node node) {
        await MarkTasksStoppedEarly(node);
        await _context.NodeTasksOperations.ClearByMachineId(node.MachineId);
        await _context.NodeMessageOperations.ClearMessages(node.MachineId);
        await base.Delete(node);

        await _context.Events.SendEvent(new EventNodeDeleted(node.MachineId, node.ScalesetId, node.PoolName, node.State));
    }

    public async Async.Task StopTask(Guid task_id) {
        // For now, this just re-images the node.  Eventually, this
        // should send a message to the node to let the agent shut down
        // gracefully

        var nodes = _context.NodeTasksOperations.GetNodesByTaskId(task_id);

        await foreach (var node in nodes) {
            await _context.NodeMessageOperations.SendMessage(node.MachineId, new NodeCommand(StopTask: new StopTaskNodeCommand(task_id)));

            if (!(await StopIfComplete(node))) {
                _logTracer.Info($"nodes: stopped task on node, but not reimaging due to other tasks: task_id:{task_id} machine_id:{node.MachineId}");
            }
        }

    }

    public async Task<OneFuzzResult<bool>> AddSshPublicKey(Node node, string publicKey) {
        if (publicKey == null) {
            throw new ArgumentNullException(nameof(publicKey));
        }

        if (node.ScalesetId == null) {
            return OneFuzzResult<bool>.Error(ErrorCode.INVALID_REQUEST, "only able to add ssh keys to scaleset nodes");
        }

        var key = publicKey.EndsWith('\n') ? publicKey : $"{publicKey}\n";

        await SendMessage(node, new NodeCommand { AddSshKey = new NodeCommandAddSshKey(key) });

        return OneFuzzResult.Ok<bool>(true);
    }

    /// returns True on stopping the node and False if this doesn't stop the node
    private async Task<bool> StopIfComplete(Node node, bool done = false) {
        var nodeTaskIds = await _context.NodeTasksOperations.GetByMachineId(node.MachineId).Select(nt => nt.TaskId).ToArrayAsync();
        var tasks = _context.TaskOperations.GetByTaskIds(nodeTaskIds);
        await foreach (var task in tasks) {
            if (!TaskStateHelper.ShuttingDown(task.State)) {
                return false;
            }
        }
        _logTracer.Info($"node: stopping busy node with all tasks complete: {node.MachineId}");

        await Stop(node, done: done);
        return true;
    }
}


public interface INodeTasksOperations : IStatefulOrm<NodeTasks, NodeTaskState> {
    IAsyncEnumerable<Node> GetNodesByTaskId(Guid taskId);
    IAsyncEnumerable<NodeAssignment> GetNodeAssignments(Guid taskId);
    IAsyncEnumerable<NodeTasks> GetByMachineId(Guid machineId);
    IAsyncEnumerable<NodeTasks> GetByTaskId(Guid taskId);
    Async.Task ClearByMachineId(Guid machineId);
}

public class NodeTasksOperations : StatefulOrm<NodeTasks, NodeTaskState, NodeTasksOperations>, INodeTasksOperations {

    ILogTracer _log;

    public NodeTasksOperations(ILogTracer log, IOnefuzzContext context)
        : base(log, context) {
        _log = log;
    }

    //TODO: suggest by Cheick: this can probably be optimize by query all NodesTasks then query the all machine in single request

    public async IAsyncEnumerable<Node> GetNodesByTaskId(Guid taskId) {
        await foreach (var entry in QueryAsync(Query.RowKey(taskId.ToString()))) {
            var node = await _context.NodeOperations.GetByMachineId(entry.MachineId);
            if (node is not null) {
                yield return node;
            }
        }
    }

    public async IAsyncEnumerable<NodeAssignment> GetNodeAssignments(Guid taskId) {

        await foreach (var entry in QueryAsync(Query.RowKey(taskId.ToString()))) {
            var node = await _context.NodeOperations.GetByMachineId(entry.MachineId);
            if (node is not null) {
                var nodeAssignment = new NodeAssignment(node.MachineId, node.ScalesetId, entry.State);
                yield return nodeAssignment;
            }
        }
    }

    public IAsyncEnumerable<NodeTasks> GetByMachineId(Guid machineId) {
        return QueryAsync(Query.PartitionKey(machineId.ToString()));
    }

    public IAsyncEnumerable<NodeTasks> GetByTaskId(Guid taskId) {
        return QueryAsync(Query.RowKey(taskId.ToString()));
    }

    public async Async.Task ClearByMachineId(Guid machineId) {
        _logTracer.Info($"clearing tasks for node {machineId}");
        await foreach (var entry in GetByMachineId(machineId)) {
            var res = await Delete(entry);
            if (!res.IsOk) {
                _logTracer.WithHttpStatus(res.ErrorV).Error($"failed to delete node task entry for machine_id: {entry.MachineId}");
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

    Async.Task SendMessage(Guid machineId, NodeCommand message, string? messageId = null);
}


public class NodeMessageOperations : Orm<NodeMessage>, INodeMessageOperations {

    private readonly ILogTracer _log;
    public NodeMessageOperations(ILogTracer log, IOnefuzzContext context) : base(log, context) {
        _log = log;
    }

    public IAsyncEnumerable<NodeMessage> GetMessage(Guid machineId)
        => QueryAsync(Query.PartitionKey(machineId.ToString()));

    public async Async.Task ClearMessages(Guid machineId) {
        _logTracer.Info($"clearing messages for node {machineId}");

        await foreach (var message in GetMessage(machineId)) {
            var r = await Delete(message);
            if (!r.IsOk) {
                _logTracer.WithHttpStatus(r.ErrorV).Error($"failed to delete message for node {machineId}");
            }
        }
    }

    public async Async.Task SendMessage(Guid machineId, NodeCommand message, string? messageId = null) {
        messageId = messageId ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        await Insert(new NodeMessage(machineId, messageId, message));
    }
}
