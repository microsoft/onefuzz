namespace Microsoft.OneFuzz.Service;

public record ShrinkEntry(Guid ShrinkId);

public sealed class ShrinkQueue {
    readonly IQueue _queueOps;
    readonly ILogTracer _log;

    public ShrinkQueue(ScalesetId baseId, IQueue queueOps, ILogTracer log)
    // backwards compat
    // scaleset ID used to be a GUID and then this class would format it with "N" format
    // to retain the same behaviour remove any dashes in the name
        : this(baseId.ToString().Replace("-", ""), queueOps, log) { }

    public ShrinkQueue(Guid poolId, IQueue queueOps, ILogTracer log)
        : this(poolId.ToString("N"), queueOps, log) { }

    private ShrinkQueue(string baseId, IQueue queueOps, ILogTracer log) {
        var name = ShrinkQueueNamePrefix + baseId;

        // queue names can be no longer than 64 characters
        // if we exceed that, trim off the end. we will still have
        // sufficient random chracters to stop collisions from happening
        if (name.Length > 64) {
            name = name[..64];
        }

        QueueName = name;
        _queueOps = queueOps;
        _log = log;
    }

    public static string ShrinkQueueNamePrefix => "to-shrink-";

    public override string ToString()
        => QueueName;

    public string QueueName { get; }

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

    public async Async.Task SetSize(long size) {
        await Clear();
        var i = 0L;

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

    public async Async.Task<bool> ShouldShrink() {
        return await _queueOps.RemoveFirstMessage(QueueName, StorageType.Config);
    }
}
