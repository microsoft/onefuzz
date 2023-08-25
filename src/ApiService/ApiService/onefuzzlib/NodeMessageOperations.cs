using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Microsoft.Extensions.Logging;
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
    IAsyncEnumerable<NodeMessage> GetMessages(Guid machineId);

    Async.Task<NodeMessage?> GetMessage(Guid machineId);
    Async.Task ClearMessages(Guid machineId);

    Async.Task SendMessage(Guid machineId, NodeCommand message, string? messageId = null);
}

public class NodeMessageOperations : Orm<NodeMessage>, INodeMessageOperations {

    public NodeMessageOperations(ILogger<NodeMessageOperations> log, IOnefuzzContext context)
        : base(log, context) { }

    public IAsyncEnumerable<NodeMessage> GetMessages(Guid machineId)
        => QueryAsync(Query.PartitionKey(machineId.ToString()));

    public async Async.Task ClearMessages(Guid machineId) {
        _logTracer.LogInformation("clearing messages for node {MachineId}", machineId);

        var result = await DeleteAll(new (string?, string?)[] { (machineId.ToString(), null) });

        if (result.FailureCount > 0) {
            _logTracer.LogError("failed to delete {FailedDeleteMessageCount} messages for node {MachineId}", result.FailureCount, machineId);
        }
    }

    public async Async.Task SendMessage(Guid machineId, NodeCommand message, string? messageId = null) {
        messageId ??= EntityBase.NewSortedKey;
        var r = await Insert(new NodeMessage(machineId, messageId, message));
        if (!r.IsOk) {
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("failed to insert message with id: {MessageId} for machine id: {MachineId} message: {Message}", messageId, machineId, message);
        }
    }

    public async Task<NodeMessage?> GetMessage(Guid machineId)
        => await QueryAsync(Query.PartitionKey(machineId.ToString()), maxPerPage: 1).FirstOrDefaultAsync();
}
