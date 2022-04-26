using Microsoft.Azure.Functions.Worker;

namespace Microsoft.OneFuzz.Service;

public class TimerRepro
{
    private readonly ILogTracer _log;

    private readonly IStorage _storage;

    private readonly IReproOperations _reproOperations;

    public TimerRepro(ILogTracer log, IStorage storage, IReproOperations reproOperations)
    {
        _log = log;
        _storage = storage;
        _reproOperations = reproOperations;
    }

    public async Async.Task Run([TimerTrigger("00:00:30")] TimerInfo myTimer)
    {
        var expired = _reproOperations.SearchExpired();
        await foreach (var repro in expired)
        {
            _log.Info($"stopping repro: {repro.VmId}");
            await _reproOperations.Stopping(repro);
        }

        var expiredVmIds = expired.Select(repro => repro?.VmId);

        await foreach (var repro in _reproOperations.SearchStates(VmStateHelper.NeedsWork()))
        {
            if (await expiredVmIds.ContainsAsync(repro.VmId))
            {
                // this VM already got processed during the expired phase
                continue;
            }
            _log.Info($"update repro: {repro.VmId}");
            await _reproOperations.ProcessStateUpdates(repro);
        }
    }

}
