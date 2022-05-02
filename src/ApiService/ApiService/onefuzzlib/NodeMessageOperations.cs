using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface INodeMessageOperations : IOrm<NodeMessage> {
    IAsyncEnumerable<NodeMessage> GetMessages(Guid machineId, int? numResults = null);
    Async.Task ClearMessages(Guid machineId);

    Async.Task SendMessage(Guid machineId, NodeCommand message, string? messageId = null);
}

public class NodeMessageOperations : Orm<NodeMessage>, INodeMessageOperations {
    public NodeMessageOperations(IStorage storage, ILogTracer log, IServiceConfig config)
        : base(storage, log, config) {
    }

    public IAsyncEnumerable<NodeMessage> GetMessages(Guid machineId, int? numResults = null) {
        return QueryAsync(filter: $"machine_id eq '{machineId}'", numResults);
    }

    public async Async.Task ClearMessages(Guid machineId) {
        _logTracer.Info($"clearing messages for node: {machineId}");
        var messages = GetMessages(machineId);
        await foreach (var message in messages) {
            await Delete(message);
        }
    }

    public async Async.Task SendMessage(Guid machineId, NodeCommand message, string? messageId = null) {
        messageId = messageId ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        await Insert(new NodeMessage(machineId, messageId, message));
    }
}
