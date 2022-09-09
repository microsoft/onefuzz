using ApiService.OneFuzzLib.Orm;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

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

    public NodeMessageOperations(ILogTracer log, IOnefuzzContext context)
        : base(log, context) { }

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
