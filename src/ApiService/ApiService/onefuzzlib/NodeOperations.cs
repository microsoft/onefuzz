using System.Net;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure;
using Azure.Data.Tables;

namespace Microsoft.OneFuzz.Service;

public interface INodeOperations : IStatefulOrm<Node, NodeState> {
    Task<Node?> GetByMachineId(Guid machineId);

    Task<bool> CanProcessNewWork(Node node);

    Task<OneFuzzResult<Node>> AcquireScaleInProtection(Node node);
    Task<OneFuzzResultVoid> ReleaseScaleInProtection(Node node);

    bool IsOutdated(Node node);
    Async.Task<Node> Stop(Node node, bool done = false);
    bool IsTooOld(Node node);
    Task<bool> CouldShrinkScaleset(Node node);
    Async.Task<Node> SetHalt(Node node);
    Async.Task<Node> SetState(Node node, NodeState state);
    Async.Task<Node> ToReimage(Node node, bool done = false);
    Async.Task SendStopIfFree(Node node);
    IAsyncEnumerable<Node> SearchStates(Guid? poolId = default,
        Guid? scalesetId = default,
        IEnumerable<NodeState>? states = default,
        PoolName? poolName = default,
        bool excludeUpdateScheduled = false,
        int? numResults = default);

    new Async.Task Delete(Node node);

    Async.Task ReimageLongLivedNodes(Guid scaleSetId);

    Async.Task<Node?> Create(
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

    Async.Task<Node> SetShutdown(Node node);

    // state transitions:
    Async.Task<Node> Init(Node node);
    Async.Task<Node> Free(Node node);
    Async.Task<Node> SettingUp(Node node);
    Async.Task<Node> Rebooting(Node node);
    Async.Task<Node> Ready(Node node);
    Async.Task<Node> Busy(Node node);
    Async.Task<Node> Done(Node node);
    Async.Task<Node> Shutdown(Node node);
    Async.Task<Node> Halt(Node node);
}


/// Future work:
///
/// Enabling autoscaling for the scalesets based on the pool work queues.
/// https://docs.microsoft.com/en-us/azure/azure-monitor/platform/autoscale-common-metrics#commonly-used-storage-metrics

public class NodeOperations : StatefulOrm<Node, NodeState, NodeOperations>, INodeOperations {

    public NodeOperations(ILogTracer log, IOnefuzzContext context)
        : base(log, context) {
    }

    public async Task<OneFuzzResult<Node>> AcquireScaleInProtection(Node node) {
        if (node.ScalesetId is Guid scalesetId &&
            await TryGetNodeInfo(node) is NodeInfo nodeInfo) {

            _logTracer.Info($"Setting scale-in protection on node {node.MachineId:Tag:MachineId}");

            var instanceId = node.InstanceId;
            if (instanceId is null) {
                var instanceIdResult = await _context.VmssOperations.GetInstanceId(scalesetId, node.MachineId);
                if (!instanceIdResult.IsOk) {
                    return instanceIdResult.ErrorV;
                }

                instanceId = instanceIdResult.OkV;

                // update stored value so it will be present later
                node = node with { InstanceId = instanceId };
                _ = await Update(node); // result ignored: this is best-effort
            }

            var r = await _context.VmssOperations.UpdateScaleInProtection(nodeInfo.Scaleset, instanceId, protectFromScaleIn: true);
            if (!r.IsOk) {
                _logTracer.Error(r.ErrorV);
            }
        }

        return OneFuzzResult.Ok(node);
    }

    public async Task<OneFuzzResultVoid> ReleaseScaleInProtection(Node node) {
        if (!node.DebugKeepNode &&
            node.ScalesetId is Guid scalesetId &&
            await TryGetNodeInfo(node) is NodeInfo nodeInfo) {

            _logTracer.Info($"Removing scale-in protection on node {node.MachineId:Tag:MachineId}");

            var instanceId = node.InstanceId;
            if (instanceId is null) {
                var instanceIdResult = await _context.VmssOperations.GetInstanceId(scalesetId, node.MachineId);
                if (!instanceIdResult.IsOk) {
                    return instanceIdResult.ErrorV;
                }

                instanceId = instanceIdResult.OkV;
            }

            var r = await _context.VmssOperations.UpdateScaleInProtection(nodeInfo.Scaleset, instanceId, protectFromScaleIn: false);
            if (!r.IsOk) {
                _logTracer.Error(r.ErrorV);
            }
            return r;
        }

        return OneFuzzResultVoid.Ok;
    }

    record NodeInfo(Node Node, Scaleset Scaleset, string InstanceId);
    private async Async.Task<NodeInfo?> TryGetNodeInfo(Node node) {
        var scalesetId = node.ScalesetId;
        if (scalesetId is null) {
            return null;
        }

        var scalesetResult = await _context.ScalesetOperations.GetById(scalesetId.Value);
        if (!scalesetResult.IsOk || scalesetResult.OkV == null) {
            return null;
        }

        // try to use stored value, if present
        var instanceId = node.InstanceId;
        if (instanceId is null) {
            var instanceIdResult = await _context.VmssOperations.GetInstanceId(scalesetResult.OkV.ScalesetId, node.MachineId);
            if (!instanceIdResult.IsOk) {
                return null;
            }

            instanceId = instanceIdResult.OkV;
        }

        return new NodeInfo(node, scalesetResult.OkV, instanceId);
    }

    public async Task<bool> CanProcessNewWork(Node node) {
        if (IsOutdated(node) && _context.ServiceConfiguration.OneFuzzAllowOutdatedAgent != "true") {
            _logTracer.Info($"can_process_new_work agent and service versions differ, stopping node. {node.MachineId:Tag:MachineId} {node.Version:Tag:AgentVersion} {_context.ServiceConfiguration.OneFuzzVersion:Tag:ServiceVersion}");
            _ = await Stop(node, done: true);
            return false;
        }

        if (IsTooOld(node)) {
            _logTracer.Info($"can_process_new_work node is too old {node.MachineId:Tag:MachineId}");
            _ = await Stop(node, done: true);
            return false;
        }

        if (!node.State.CanProcessNewWork()) {
            _logTracer.Info($"can_process_new_work node not in appropriate state for new work {node.MachineId:Tag:MachineId} {node.State:Tag:State}");
            return false;
        }

        if (node.State.ReadyForReset()) {
            _logTracer.Info($"can_process_new_work node is set for reset {node.MachineId:Tag:MachineId}");
            return false;
        }

        if (node.DeleteRequested) {
            _logTracer.Info($"can_process_new_work is set to be deleted {node.MachineId:Tag:MachineId}");
            _ = await Stop(node, done: true);
            return false;
        }

        if (node.ReimageRequested) {
            _logTracer.Info($"can_process_new_work is set to be reimaged {node.MachineId:Tag:MachineId}");
            _ = await Stop(node, done: true);
            return false;
        }

        if (await CouldShrinkScaleset(node)) {
            _logTracer.Info($"can_process_new_work node scheduled to shrink {node.MachineId:Tag:MachineId}");
            _ = await SetHalt(node);
            return false;
        }

        if (node.ScalesetId != null) {
            var scalesetResult = await _context.ScalesetOperations.GetById(node.ScalesetId.Value);
            if (!scalesetResult.IsOk) {
                _logTracer.Info($"can_process_new_work invalid scaleset {node.ScalesetId:Tag:ScalesetId} - {node.MachineId:Tag:MachineId}");
                return false;
            }

            var scaleset = scalesetResult.OkV;
            if (!scaleset.State.IsAvailable()) {
                _logTracer.Info($"can_process_new_work scaleset not available for work {scaleset.ScalesetId:Tag:ScalesetId} - {node.MachineId:Tag:MachineId} {scaleset.State:Tag:State}");
                return false;
            }
        }

        var poolResult = await _context.PoolOperations.GetByName(node.PoolName);
        if (!poolResult.IsOk) {
            _logTracer.Info($"can_schedule - invalid pool {node.PoolName:Tag:PoolName} - {node.MachineId:Tag:MachineId}");
            return false;
        }

        var pool = poolResult.OkV;
        if (!PoolStateHelper.Available.Contains(pool.State)) {
            _logTracer.Info($"can_schedule - pool is not available for work {node.PoolName:Tag:PoolName} - {node.MachineId:Tag:MachineId}");
            return false;
        }

        return true;
    }


    /// Mark any excessively long lived node to be re-imaged.
    /// This helps keep nodes on scalesets that use `latest` OS image SKUs
    /// reasonably up-to-date with OS patches without disrupting running
    /// fuzzing tasks with patch reboot cycles.
    public async Async.Task ReimageLongLivedNodes(Guid scaleSetId) {
        var timeFilter = Query.OlderThan("initialized_at", DateTimeOffset.UtcNow - INodeOperations.NODE_REIMAGE_TIME);
        //force ToString(), since all GUIDs are strings in the table
        await foreach (var node in QueryAsync(Query.And(TableClient.CreateQueryFilter($"scaleset_id eq {scaleSetId.ToString()}"), timeFilter))) {
            if (node.DebugKeepNode) {
                _logTracer.Info($"removing debug_keep_node for expired node. scaleset_id:{node.ScalesetId} machine_id:{node.MachineId}");
            }
            _ = await ToReimage(node with { DebugKeepNode = false });
        }
    }


    public async Async.Task MarkOutdatedNodes() {
        //if outdated agents are allowed, do not attempt to update
        var parsed = bool.TryParse(_context.ServiceConfiguration.OneFuzzAllowOutdatedAgent ?? "false", out var allowOutdatedAgent);
        if (parsed && allowOutdatedAgent) {
            return;
        }

        var outdated = SearchOutdated(excludeUpdateScheduled: true);
        await foreach (var node in outdated) {
            _logTracer.Info($"node is outdated: {node.MachineId:Tag:MachineId} - {node.Version:Tag:NodeVersion}");

            if (node.Version == "1.0.0") {
                _ = await ToReimage(node, done: true);
            } else {
                _ = await ToReimage(node);
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
            queryParts.Add(TableClient.CreateQueryFilter($"(pool_id eq {poolId})"));
        }

        if (poolName is not null) {
            queryParts.Add(TableClient.CreateQueryFilter($"(pool_name eq {poolName.String})"));
        }

        if (scalesetId is not null) {
            queryParts.Add(TableClient.CreateQueryFilter($"(scaleset_id eq {scalesetId})"));
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
            _ = await StopIfComplete(node, true);
        }
    }

    public async Async.Task<Node> ToReimage(Node node, bool done = false) {

        var nodeState = node.State;
        if (done) {
            if (!node.State.ReadyForReset()) {
                nodeState = NodeState.Done;
            }
        }

        var reimageRequested = node.ReimageRequested;
        if (!node.ReimageRequested && !node.DeleteRequested) {
            _logTracer.Info($"setting reimage_requested: {node.MachineId:Tag:MachineId} {node.ScalesetId:Tag:ScalesetId}");
            reimageRequested = true;
        }

        var updatedNode = node with { State = nodeState, ReimageRequested = reimageRequested };
        //if we're going to reimage, make sure the node doesn't pick up new work too.
        await SendStopIfFree(updatedNode);

        var r = await Replace(updatedNode);
        if (!r.IsOk) {
            _logTracer.WithHttpStatus(r.ErrorV).Error($"Failed to save Node record for node {updatedNode.MachineId:Tag:MachineId} {node.ScalesetId:Tag:ScalesetId}");
        }

        return updatedNode;
    }

    public IAsyncEnumerable<Node> GetDeadNodes(Guid scaleSetId, TimeSpan expirationPeriod) {
        var minDate = DateTimeOffset.UtcNow - expirationPeriod;

        var filter = $"heartbeat lt datetime'{minDate.ToString("o")}' or Timestamp lt datetime'{minDate.ToString("o")}'";
        var query = Query.And(filter, $"scaleset_id eq '{scaleSetId}'");
        return QueryAsync(query);
    }


    public async Async.Task<Node?> Create(
        Guid poolId,
        PoolName poolName,
        Guid machineId,
        Guid? scaleSetId,
        string version,
        bool isNew = false) {

        var node = new Node(poolName, machineId, poolId, version, ScalesetId: scaleSetId);

        ResultVoid<(int, string)> r;
        if (isNew) {
            try {
                r = await Insert(node);
            } catch (RequestFailedException ex) when (
                ex.Status == (int)HttpStatusCode.Conflict ||
                ex.ErrorCode == "EntityAlreadyExists") {

                var existingNode = await QueryAsync(Query.SingleEntity(poolName.ToString(), machineId.ToString())).FirstOrDefaultAsync();
                if (existingNode is not null) {
                    if (existingNode.State != node.State || existingNode.ReimageRequested != node.ReimageRequested || existingNode.Version != node.Version || existingNode.DeleteRequested != node.DeleteRequested) {
                        _logTracer.Error($"Not replacing {existingNode:Tag:ExistingNode} with a new-and-different {node:Tag:Node}");
                    }
                    return null;
                } else {
                    _logTracer.Critical($"Failed to get node when node insertion returned EntityAlreadyExists {poolName.ToString():Tag:PoolName} {machineId:Tag:MachineId}");
                    r = await Replace(node);
                }
            }
        } else {
            r = await Replace(node);
        }

        if (!r.IsOk) {
            _logTracer.WithHttpStatus(r.ErrorV).Error($"failed to save NodeRecord for node {node.MachineId:Tag:MachineId} isNew: {isNew:Tag:IsNew}");
            return null;
        }

        await _context.Events.SendEvent(
            new EventNodeCreated(
                node.MachineId,
                node.ScalesetId,
                node.PoolName));

        return node;
    }

    public async Async.Task<Node> Stop(Node node, bool done = false) {
        node = await ToReimage(node, done);
        await SendMessage(node, new NodeCommand(Stop: new StopNodeCommand()));
        return node;
    }

    /// <summary>
    ///  Tell node to stop everything
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    public async Async.Task<Node> SetHalt(Node node) {
        _logTracer.Info($"setting halt: {node.MachineId:Tag:MachineId}");
        node = node with { DeleteRequested = true };
        node = await Stop(node, true);
        await SendStopIfFree(node);
        return node;
    }

    public async Async.Task<Node> SetShutdown(Node node) {
        //don't give out more work to the node, but let it finish existing work
        _logTracer.Info($"setting delete_requested: {node.MachineId:Tag:MachineId}");
        node = node with { DeleteRequested = true };
        var r = await Replace(node);
        if (!r.IsOk) {
            _logTracer.WithHttpStatus(r.ErrorV).Error($"failed to update node with delete requested. {node.MachineId:Tag:MachineId} {node.PoolName:Tag:PoolName} {node.PoolId:Tag:PoolId} {node.ScalesetId:Tag:ScalesetId}");
        }

        await SendStopIfFree(node);
        return node;
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
            _logTracer.WithHttpStatus(r.ErrorV).Error($"failed to replace NodeMessge record for {node.MachineId:Tag:MachineId}");
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

    public async Async.Task<Node> SetState(Node node, NodeState state) {
        if (node.State != state) {
            _logTracer.Event($"SetState Node {node.MachineId:Tag:MachineId} {node.State:Tag:From} - {state:Tag:To}");

            node = node with { State = state };
            await _context.Events.SendEvent(new EventNodeStateUpdated(
                node.MachineId,
                node.ScalesetId,
                node.PoolName,
                state
            ));
        }

        var r = await Update(node);
        if (!r.IsOk) {
            _logTracer.Error($"Failed to update node for: {node.MachineId:Tag:MachineId} {state:Tag:State} due to {r.ErrorV:Tag:Error}");
            // TODO: this should error out
        }

        return node;
    }

    public static string SearchStatesQuery(
        Guid? poolId = default,
        Guid? scaleSetId = default,
        IEnumerable<NodeState>? states = default,
        PoolName? poolName = default,
        int? numResults = default) {

        List<string> queryParts = new();

        if (poolId is not null) {
            queryParts.Add($"(pool_id eq '{poolId}')");
        }

        if (poolName is not null) {
            queryParts.Add($"(PartitionKey eq '{poolName.String}')");
        }

        if (scaleSetId is not null) {
            queryParts.Add($"(scaleset_id eq '{scaleSetId}')");
        }

        if (states is not null) {
            var q = Query.EqualAnyEnum("state", states);
            queryParts.Add($"({q})");
        }

        return Query.And(queryParts);
    }


    public IAsyncEnumerable<Node> SearchStates(
        Guid? poolId = default,
        Guid? scalesetId = default,
        IEnumerable<NodeState>? states = default,
        PoolName? poolName = default,
        bool excludeUpdateScheduled = false,
        int? numResults = default) {
        var query = NodeOperations.SearchStatesQuery(poolId, scalesetId, states, poolName, numResults);

        if (numResults is null) {
            return QueryAsync(query);
        } else {
            return QueryAsync(query).Take(numResults.Value);
        }
    }

    public IAsyncEnumerable<Node> SearchByPoolName(PoolName poolName) {
        return QueryAsync(TableClient.CreateQueryFilter($"(pool_name eq {poolName.String})"));
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
                var r = await _context.NodeTasksOperations.Delete(entry);
                if (!r.IsOk) {
                    _logTracer.WithHttpStatus(r.ErrorV).Error($"failed to delete task operation for {entry.TaskId:Tag:TaskId}");
                }
            }
        }
    }

    public new async Async.Task Delete(Node node) {
        await MarkTasksStoppedEarly(node);
        await _context.NodeTasksOperations.ClearByMachineId(node.MachineId);
        await _context.NodeMessageOperations.ClearMessages(node.MachineId);
        var r = await base.Delete(node);
        if (!r.IsOk) {
            _logTracer.WithHttpStatus(r.ErrorV).Error($"Failed to delete node {node.MachineId:Tag:MachineId}");
        }

        await _context.Events.SendEvent(new EventNodeDeleted(node.MachineId, node.ScalesetId, node.PoolName, node.State));
    }

    public async Async.Task StopTask(Guid task_id) {
        // For now, this just re-images the node.  Eventually, this
        // should send a message to the node to let the agent shut down
        // gracefully

        var nodes = _context.NodeTasksOperations.GetNodesByTaskId(task_id);

        await foreach (var node in nodes) {
            await _context.NodeMessageOperations.SendMessage(node.MachineId, new NodeCommand(StopTask: new StopTaskNodeCommand(task_id)));

            if (!await StopIfComplete(node)) {
                _logTracer.Info($"nodes: stopped task on node, but not reimaging due to other tasks: {task_id:Tag:TaskId} {node.MachineId:Tag:MachineId}");
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
        _logTracer.Info($"node: stopping busy node with all tasks complete: {node.MachineId:Tag:MachineId}");

        _ = await Stop(node, done: done);
        return true;
    }

    public Task<Node> Init(Node node) {
        // nothing to do
        return Async.Task.FromResult(node);
    }

    public Task<Node> Free(Node node) {
        // nothing to do
        return Async.Task.FromResult(node);
    }

    public Task<Node> SettingUp(Node node) {
        // nothing to do
        return Async.Task.FromResult(node);
    }

    public Task<Node> Rebooting(Node node) {
        // nothing to do
        return Async.Task.FromResult(node);
    }

    public Task<Node> Ready(Node node) {
        // nothing to do
        return Async.Task.FromResult(node);
    }

    public Task<Node> Busy(Node node) {
        // nothing to do
        return Async.Task.FromResult(node);
    }

    public Task<Node> Done(Node node) {
        // nothing to do
        return Async.Task.FromResult(node);
    }

    public Task<Node> Shutdown(Node node) {
        // nothing to do
        return Async.Task.FromResult(node);
    }

    public Task<Node> Halt(Node node) {
        // nothing to do
        return Async.Task.FromResult(node);
    }
}
