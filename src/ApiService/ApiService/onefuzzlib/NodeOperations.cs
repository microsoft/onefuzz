using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;

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
}

public class NodeOperations : StatefulOrm<Node, NodeState>, INodeOperations {

    // 1 hour
    private static readonly TimeSpan NODE_EXPIRATION_TIME = new TimeSpan(1, 0, 0);

    // 6 days
    private static readonly TimeSpan NODE_REIMAGE_TIME = new TimeSpan(6, 0, 0, 0);
    private IScalesetOperations _scalesetOperations;
    private IPoolOperations _poolOperations;
    private INodeMessageOperations _nodeMessageOperations;

    private IEvents _eventOperations;

    public NodeOperations(IStorage storage, ILogTracer log, IServiceConfig config, IScalesetOperations scalesetOperations, IPoolOperations poolOperations, INodeMessageOperations nodeMessageOperations, IEvents eventOperations)
        : base(storage, log, config) {

        _scalesetOperations = scalesetOperations;
        _poolOperations = poolOperations;
        _nodeMessageOperations = nodeMessageOperations;
        _eventOperations = eventOperations;
    }

    public Task<OneFuzzResultVoid> AcquireScaleInProtection(Node node) {
        throw new NotImplementedException();
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

        if (!NodeStateHelper.CanProcessNewWork().Contains(node.State)) {
            _logTracer.Info($"can_process_new_work node not in appropriate state for new work machine_id:{node.MachineId} state:{node.State}");
            return false;
        }

        if (NodeStateHelper.ReadyForReset().Contains(node.State)) {
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
            await _eventOperations.SendEvent(new EventNodeStateUpdated(
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
        if (done && !NodeStateHelper.ReadyForReset().Contains(node.State)) {
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
        await _nodeMessageOperations.SendMessage(node.MachineId, message);
    }
}
