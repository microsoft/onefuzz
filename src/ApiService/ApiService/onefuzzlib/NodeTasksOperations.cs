using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;

public interface INodeTasksOperations : IStatefulOrm<NodeTasks, NodeTaskState> {
    IAsyncEnumerable<Node> GetNodesByTaskId(Guid taskId);
    IAsyncEnumerable<NodeAssignment> GetNodeAssignments(Guid taskId);
    IAsyncEnumerable<NodeTasks> GetByMachineId(Guid machineId);
    IAsyncEnumerable<NodeTasks> GetByTaskId(Guid taskId);
    Async.Task ClearByMachineId(Guid machineId);

    // state transitions:
    Async.Task<NodeTasks> Init(NodeTasks nodeTasks);
    Async.Task<NodeTasks> SettingUp(NodeTasks nodeTasks);
    Async.Task<NodeTasks> Running(NodeTasks nodeTasks);
}

public class NodeTasksOperations : StatefulOrm<NodeTasks, NodeTaskState, NodeTasksOperations>, INodeTasksOperations {

    public NodeTasksOperations(ILogger<NodeTasksOperations> log, IOnefuzzContext context)
        : base(log, context) {
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
        _logTracer.LogInformation("clearing tasks for node {MachineId}", machineId);
        await foreach (var entry in GetByMachineId(machineId)) {
            var res = await Delete(entry);
            if (!res.IsOk) {
                _logTracer.AddHttpStatus(res.ErrorV);
                _logTracer.LogError("failed to delete node task entry for {MachineId}", entry.MachineId);
            }
        }
    }

    public Task<NodeTasks> Init(NodeTasks nodeTasks) {
        // nothing to do
        return Async.Task.FromResult(nodeTasks);
    }

    public Task<NodeTasks> SettingUp(NodeTasks nodeTasks) {
        // nothing to do
        return Async.Task.FromResult(nodeTasks);
    }

    public Task<NodeTasks> Running(NodeTasks nodeTasks) {
        // nothing to do
        return Async.Task.FromResult(nodeTasks);
    }
}
