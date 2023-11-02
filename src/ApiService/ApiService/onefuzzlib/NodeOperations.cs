using System.Net;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;

public record CanProcessNewWorkResponse(bool IsAllowed, string? Reason) {
    public static CanProcessNewWorkResponse Allowed() => new CanProcessNewWorkResponse(true, null);
    public static CanProcessNewWorkResponse NotAllowed(string reason) => new CanProcessNewWorkResponse(false, reason);
};

public interface INodeOperations : IStatefulOrm<Node, NodeState> {
    Task<Node?> GetByMachineId(Guid machineId);

    Task<CanProcessNewWorkResponse> CanProcessNewWork(Node node);

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
        ScalesetId? scalesetId = default,
        IEnumerable<NodeState>? states = default,
        PoolName? poolName = default,
        bool excludeUpdateScheduled = false,
        int? numResults = default);

    Async.Task Delete(Node node, string reason);

    Async.Task ReimageLongLivedNodes(ScalesetId scaleSetId);

    Async.Task<Node?> Create(
        Guid poolId,
        PoolName poolName,
        Guid machineId,
        string? instanceId,
        ScalesetId? scaleSetId,
        string version,
        bool isNew = false);

    IAsyncEnumerable<Node> GetDeadNodes(ScalesetId scaleSetId, TimeSpan expirationPeriod);

    Async.Task MarkTasksStoppedEarly(Node node, Error? error);
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

    public NodeOperations(ILogger<NodeOperations> log, IOnefuzzContext context)
        : base(log, context) {
    }

    public async Task<OneFuzzResult<Node>> AcquireScaleInProtection(Node node) {
        if (node.ScalesetId is ScalesetId scalesetId &&
            await TryGetNodeInfo(node) is NodeInfo nodeInfo) {

            var metricDimensions = new Dictionary<string, string> {
                {"MachineId", node.MachineId.ToString()}
            };
            _logTracer.AddTags(metricDimensions);
            _logTracer.LogInformation("Setting scale-in protection on node {MachineId}", node.MachineId);

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
                switch (r.ErrorV.Code) {
                    case ErrorCode.SCALE_IN_PROTECTION_UPDATE_ALREADY_IN_PROGRESS:
                        _logTracer.LogWarning("Transiently failed to modify scale-in protection: {}", r.ErrorV);
                        break;
                    default:
                        _logTracer.LogOneFuzzError(r.ErrorV);
                        break;
                }
                _logTracer.LogMetric("FailedAcquiringScaleInProtection", 1);
                return r.ErrorV;
            }

            _logTracer.LogMetric("AcquiredScaleInProtection", 1);
            return OneFuzzResult.Ok(node);
        }

        return Error.Create(ErrorCode.INVALID_NODE, "Failed getting NodeInfo. Cannot acquire scale-in protection");
    }

    public async Task<OneFuzzResultVoid> ReleaseScaleInProtection(Node node) {
        if (!node.DebugKeepNode &&
            node.ScalesetId is ScalesetId scalesetId &&
            await TryGetNodeInfo(node) is NodeInfo nodeInfo) {

            var metricDimensions = new Dictionary<string, string> {
                {"MachineId", node.MachineId.ToString()}
            };
            _logTracer.AddTags(metricDimensions);
            _logTracer.LogInformation("Removing scale-in protection on node {MachineId}", node.MachineId);

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
                switch (r.ErrorV.Code) {
                    case ErrorCode.SCALE_IN_PROTECTION_INSTANCE_NO_LONGER_EXISTS:
                    case ErrorCode.SCALE_IN_PROTECTION_UPDATE_ALREADY_IN_PROGRESS:
                        _logTracer.LogWarning("Transiently failed to modify scale-in protection: {}", r.ErrorV);
                        break;
                    default:
                        _logTracer.LogOneFuzzError(r.ErrorV);
                        break;
                }
                _logTracer.LogMetric("FailedReleasingScaleInProtection", 1);
                return r;
            }

            _logTracer.LogMetric("ReleasedScaleInProection", 1);
            return r;
        }

        return OneFuzzResultVoid.Ok;
    }

    sealed record NodeInfo(Node Node, Scaleset Scaleset, string InstanceId);
    private async Async.Task<NodeInfo?> TryGetNodeInfo(Node node) {
        var scalesetId = node.ScalesetId;
        if (scalesetId is null) {
            return null;
        }

        var scalesetResult = await _context.ScalesetOperations.GetById(scalesetId);
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



    public async Task<CanProcessNewWorkResponse> CanProcessNewWork(Node node) {
        if (IsOutdated(node) && _context.ServiceConfiguration.OneFuzzAllowOutdatedAgent != "true") {
            _ = await Stop(node, done: true);
            return CanProcessNewWorkResponse.NotAllowed("agent and service versions differ");
        }

        if (IsTooOld(node)) {
            _ = await Stop(node, done: true);
            return CanProcessNewWorkResponse.NotAllowed("node is too old");
        }

        if (!node.State.CanProcessNewWork()) {
            return CanProcessNewWorkResponse.NotAllowed("node not in appropriate state for new work");
        }

        if (node.State.ReadyForReset()) {
            return CanProcessNewWorkResponse.NotAllowed("node is set for reset");
        }

        if (node.DeleteRequested) {
            _ = await Stop(node, done: true);
            return CanProcessNewWorkResponse.NotAllowed("node is set to be deleted");
        }

        if (node.ReimageRequested && node.Managed) {
            _ = await Stop(node, done: true);
            return CanProcessNewWorkResponse.NotAllowed("node is set to be reimaged");
        }

        if (await CouldShrinkScaleset(node) && node.Managed) {
            _ = await SetHalt(node);
            return CanProcessNewWorkResponse.NotAllowed("node is scheduled to shrink");
        }

        if (node.ScalesetId is not null) {
            var scalesetResult = await _context.ScalesetOperations.GetById(node.ScalesetId);
            if (!scalesetResult.IsOk) {
                return CanProcessNewWorkResponse.NotAllowed("invalid scaleset");
            }

            var scaleset = scalesetResult.OkV;
            if (!scaleset.State.IsAvailable()) {
                return CanProcessNewWorkResponse.NotAllowed($"scaleset not available for work. Scaleset state '{scaleset.State}'");
            }
        }

        var poolResult = await _context.PoolOperations.GetByName(node.PoolName);
        if (!poolResult.IsOk) {
            return CanProcessNewWorkResponse.NotAllowed("invalid pool");
        }

        var pool = poolResult.OkV;
        if (!PoolStateHelper.Available.Contains(pool.State)) {
            return CanProcessNewWorkResponse.NotAllowed("pool is not available for work");
        }

        return CanProcessNewWorkResponse.Allowed();
    }


    /// Mark any excessively long lived node to be re-imaged.
    /// This helps keep nodes on scalesets that use `latest` OS image SKUs
    /// reasonably up-to-date with OS patches without disrupting running
    /// fuzzing tasks with patch reboot cycles.
    public async Async.Task ReimageLongLivedNodes(ScalesetId scaleSetId) {
        var timeFilter = Query.OlderThan("initialized_at", DateTimeOffset.UtcNow - INodeOperations.NODE_REIMAGE_TIME);

        await foreach (var node in QueryAsync(Query.And(Query.CreateQueryFilter($"scaleset_id eq {scaleSetId}"), timeFilter))) {
            if (node.DebugKeepNode) {
                _logTracer.LogInformation("removing debug_keep_node for expired node. scaleset_id:{scaleSetId} machine_id:{machineId}", node.ScalesetId, node.MachineId);
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
        // update up to 10 nodes in parallel
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 10 };
        await Parallel.ForEachAsync(outdated, parallelOptions, async (node, _cancel) => {
            _logTracer.LogInformation("node is outdated: {MachineId} - {NodeVersion}", node.MachineId, node.Version);

            if (node.Version == "1.0.0") {
                _ = await ToReimage(node, done: true);
            } else {
                _ = await ToReimage(node);
            }
        });
    }


    public static string SearchOutdatedQuery(
    string oneFuzzVersion,
    Guid? poolId = null,
    ScalesetId? scalesetId = null,
    IEnumerable<NodeState>? states = null,
    PoolName? poolName = null,
    bool excludeUpdateScheduled = false,
    int? numResults = null) {

        List<string> queryParts = new();

        if (poolId is not null) {
            queryParts.Add(Query.CreateQueryFilter($"(pool_id eq {poolId})"));
        }

        if (poolName is not null) {
            queryParts.Add(Query.CreateQueryFilter($"(pool_name eq {poolName})"));
        }

        if (scalesetId is not null) {
            queryParts.Add(Query.CreateQueryFilter($"(scaleset_id eq {scalesetId})"));
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
        var versionQuery = Query.CreateQueryFilter($"not (version eq {oneFuzzVersion})");
        queryParts.Add(versionQuery);
        return Query.And(queryParts);
    }

    IAsyncEnumerable<Node> SearchOutdated(
            Guid? poolId = null,
            ScalesetId? scalesetId = null,
            IEnumerable<NodeState>? states = null,
            PoolName? poolName = null,
            bool excludeUpdateScheduled = false,
            int? numResults = null) {

        var query = SearchOutdatedQuery(_context.ServiceConfiguration.OneFuzzVersion, poolId, scalesetId, states, poolName, excludeUpdateScheduled, numResults);
        if (numResults is null) {
            return QueryAsync(query);
        } else {
            return QueryAsync(query).Take(numResults.Value);
        }
    }

    public async Async.Task CleanupBusyNodesWithoutWork() {
        //# There is a potential race condition if multiple `Node.stop_task` calls
        //# are made concurrently.  By performing this check regularly, any nodes
        //# that hit this race condition will get cleaned up.
        var nodes = _context.NodeOperations.SearchStates(states: NodeStateHelper.BusyStates);

        // update up to 10 nodes in parallel
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 10 };
        await Parallel.ForEachAsync(nodes, async (node, _cancel) => {
            _ = await StopIfComplete(node, true);
        });
    }

    public async Async.Task<Node> ToReimage(Node node, bool done = false) {
        if (!node.Managed) {
            _logTracer.LogInformation("skip reimage for unmanaged node: {MachineId}", node.MachineId);
            return node;
        }

        var nodeState = node.State;
        if (done) {
            if (!node.State.ReadyForReset()) {
                nodeState = NodeState.Done;
            }
        }

        var reimageRequested = node.ReimageRequested;
        if (!node.ReimageRequested && !node.DeleteRequested) {
            _logTracer.LogInformation("setting reimage_requested: {MachineId} {ScalesetId}", node.MachineId, node.ScalesetId);
            reimageRequested = true;
        }

        var updatedNode = node with { State = nodeState, ReimageRequested = reimageRequested };
        //if we're going to reimage, make sure the node doesn't pick up new work too.
        await SendStopIfFree(updatedNode);

        var r = await Replace(updatedNode);
        if (!r.IsOk) {
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("Failed to save Node record for node {MachineId} {ScalesetId}", updatedNode.MachineId, node.ScalesetId);
        }

        return updatedNode;
    }

    public IAsyncEnumerable<Node> GetDeadNodes(ScalesetId scaleSetId, TimeSpan expirationPeriod) {
        var minDate = DateTimeOffset.UtcNow - expirationPeriod;

        var filter = $"heartbeat lt datetime'{minDate.ToString("o")}' or Timestamp lt datetime'{minDate.ToString("o")}'";
        var query = Query.And(filter, Query.CreateQueryFilter($"scaleset_id eq {scaleSetId}"));
        return QueryAsync(query);
    }


    public async Async.Task<Node?> Create(
        Guid poolId,
        PoolName poolName,
        Guid machineId,
        string? instanceId,
        ScalesetId? scaleSetId,
        string version,
        bool isNew = false) {

        var node = new Node(
            poolName,
            machineId,
            poolId,
            version,
            InstanceId: instanceId,
            ScalesetId: scaleSetId);

        ResultVoid<(HttpStatusCode Status, string Reason)> r;
        if (isNew) {
            try {
                r = await Insert(node);
            } catch (RequestFailedException ex) when (
                ex.Status == (int)HttpStatusCode.Conflict ||
                ex.ErrorCode == "EntityAlreadyExists") {

                var existingNode = await QueryAsync(Query.SingleEntity(poolName.ToString(), machineId.ToString())).FirstOrDefaultAsync();
                if (existingNode is not null) {
                    if (existingNode.State != node.State || existingNode.ReimageRequested != node.ReimageRequested || existingNode.Version != node.Version || existingNode.DeleteRequested != node.DeleteRequested) {
                        _logTracer.LogError("Not replacing {ExistingNode} with a new-and-different {Node}", existingNode, node);
                    }
                    return null;
                } else {
                    _logTracer.LogCritical("Failed to get node when node insertion returned EntityAlreadyExists {PoolName} {MachineId}", poolName.ToString(), machineId);
                    r = await Replace(node);
                }
            }
        } else {
            r = await Replace(node);
        }

        if (!r.IsOk) {
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("failed to save NodeRecord for node {MachineId} isNew: {IsNew}", node.MachineId, isNew);
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
        _logTracer.LogInformation("setting halt: {MachineId}", node.MachineId);
        node = node with { DeleteRequested = true };
        node = await Stop(node, true);
        await SendStopIfFree(node);
        return node;
    }

    public async Async.Task<Node> SetShutdown(Node node) {
        //don't give out more work to the node, but let it finish existing work
        _logTracer.LogInformation("setting delete_requested: {MachineId}", node.MachineId);
        node = node with { DeleteRequested = true };
        var r = await Replace(node);
        if (!r.IsOk) {
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("failed to update node with delete requested. {MachineId} {PoolName} {PoolId} {ScalesetId}", node.MachineId, node.PoolName, node.PoolId, node.ScalesetId);
        }

        await SendStopIfFree(node);
        return node;
    }


    public async Async.Task SendStopIfFree(Node node) {
        var ver = new Version(_context.ServiceConfiguration.OneFuzzVersion.Split('-', '+')[0]);
        if (ver >= Version.Parse("2.16.1")) {
            await SendMessage(node, new NodeCommand(StopIfFree: new NodeCommandStopIfFree()));
        }
    }

    public async Async.Task SendMessage(Node node, NodeCommand message) {
        var r = await _context.NodeMessageOperations.Replace(new NodeMessage(node.MachineId, message));
        if (!r.IsOk) {
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("failed to replace NodeMessge record for {MachineId}", node.MachineId);
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
        return node.Managed
            && node.ScalesetId != null
            && node.InitializedAt != null
            && node.InitializedAt < DateTime.UtcNow - INodeOperations.NODE_REIMAGE_TIME;
    }

    public async Task<bool> CouldShrinkScaleset(Node node) {
        if (node.ScalesetId is ScalesetId scalesetId) {
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
            _logTracer.AddTags(new Dictionary<string, string>() {
                { "MachineId", node.MachineId.ToString() },
                { "From", node.State.ToString() },
                { "To", state.ToString() } }
            );
            _logTracer.LogEvent("SetState Node");

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
            _logTracer.LogError("Failed to update node for: {MachineId} {State} due to {Error}", node.MachineId, state, r.ErrorV);
            // TODO: this should error out
        }

        return node;
    }

    public static string SearchStatesQuery(
        Guid? poolId = default,
        ScalesetId? scaleSetId = default,
        IEnumerable<NodeState>? states = default,
        PoolName? poolName = default,
        int? numResults = default) {

        List<string> queryParts = new();

        if (poolId is not null) {
            queryParts.Add(Query.CreateQueryFilter($"(pool_id eq {poolId})"));
        }

        if (poolName is not null) {
            queryParts.Add(Query.CreateQueryFilter($"(PartitionKey eq {poolName})"));
        }

        if (scaleSetId is not null) {
            queryParts.Add(Query.CreateQueryFilter($"(scaleset_id eq {scaleSetId})"));
        }

        if (states is not null) {
            var q = Query.EqualAnyEnum("state", states);
            queryParts.Add($"({q})");
        }

        return Query.And(queryParts);
    }


    public IAsyncEnumerable<Node> SearchStates(
        Guid? poolId = default,
        ScalesetId? scalesetId = default,
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
        return QueryAsync(Query.CreateQueryFilter($"(pool_name eq {poolName.String})"));
    }


    public async Async.Task MarkTasksStoppedEarly(Node node, Error? error) {
        await foreach (var entry in _context.NodeTasksOperations.GetByMachineId(node.MachineId)) {
            var task = await
                (entry.JobId.HasValue
                ? _context.TaskOperations.GetByJobIdAndTaskId(entry.JobId.Value, entry.TaskId)
                // old data might not have job ID:
                : _context.TaskOperations.GetByTaskIdSlow(entry.TaskId));

            if (task is not null && !TaskStateHelper.ShuttingDown(task.State)) {
                var message = $"Node {node.MachineId} stopping while the task state is '{task.State}'";
                if (error is not null) {
                    if (error.Errors == null) {
                        error = error with { Errors = new List<string>() };
                    }
                    error.Errors.Add(message);
                } else {
                    error = Error.Create(ErrorCode.TASK_FAILED, message);
                }
                await _context.TaskOperations.MarkFailed(task, error);
            }
            if (!node.DebugKeepNode) {
                var r = await _context.NodeTasksOperations.Delete(entry);
                if (!r.IsOk) {
                    _logTracer.AddHttpStatus(r.ErrorV);
                    _logTracer.LogError("failed to delete task operation for {TaskId}", entry.TaskId);
                }
            }
        }
    }

    public async Async.Task Delete(Node node, string reason) {
        var error = Error.Create(ErrorCode.NODE_DELETED, reason, $"Node {node.MachineId} is being deleted");

        await MarkTasksStoppedEarly(node, error);
        await _context.NodeTasksOperations.ClearByMachineId(node.MachineId);
        await _context.NodeMessageOperations.ClearMessages(node.MachineId);
        var r = await base.Delete(node);
        if (!r.IsOk) {
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("Failed to delete node {MachineId}", node.MachineId);
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
                _logTracer.LogInformation("nodes: stopped task on node, but not reimaging due to other tasks: {TaskId} {MachineId}", task_id, node.MachineId);
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
        var tasks = _context.NodeTasksOperations.GetByMachineId(node.MachineId)
            .SelectAwait(async node => {
                if (node.JobId.HasValue) {
                    return await _context.TaskOperations.GetByJobIdAndTaskId(node.JobId.Value, node.TaskId);
                } else {
                    // old existing records might not have jobId - fall back to slow lookup
                    return await _context.TaskOperations.GetByTaskIdSlow(node.TaskId);
                }
            });

        await foreach (var task in tasks) {
            if (task is not null && !TaskStateHelper.ShuttingDown(task.State)) {
                return false;
            }
        }
        _logTracer.LogInformation("node: stopping busy node with all tasks complete: {MachineId}", node.MachineId);

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
