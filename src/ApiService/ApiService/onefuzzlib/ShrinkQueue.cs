namespace Microsoft.OneFuzz.Service;

public record ShrinkEntry(Guid ShrinkId);


public class ShrinkQueue {
    Guid _baseId;
    IQueue _queueOps;
    ILogTracer _log;

    public ShrinkQueue(Guid baseId, IQueue queueOps, ILogTracer log) {
        _baseId = baseId;
        _queueOps = queueOps;
        _log = log;
    }

    public override string ToString() {
        return $"to-shrink-{_baseId.ToString("N")}";
    }
    public string QueueName => this.ToString();

    public async Async.Task Clear() {
        await _queueOps.ClearQueue(QueueName, StorageType.Config);
    }

    public async Async.Task Create() {
        await _queueOps.CreateQueue(QueueName, StorageType.Config);
    }

    public async Async.Task Delete() {
        await _queueOps.DeleteQueue(QueueName, StorageType.Config);
    }

    public async Async.Task<bool> AddEntry() {
        return await _queueOps.QueueObject<ShrinkEntry>(QueueName, new ShrinkEntry(Guid.NewGuid()), StorageType.Config);
    }

    public async Async.Task SetSize(int size) {
        await Clear();
        var i = 0;

        while (i < size) {
            var r = await AddEntry();
            if (r) {
                i++;
            } else {
                //TODO: retry after a delay ? I guess make a decision on this
                //if we hit this error message... For now just log and move on to
                //make it behave same as Python code.
                _log.Error($"failed to add entry to shrink queue");
                i++;
            }
        }

    }


}
