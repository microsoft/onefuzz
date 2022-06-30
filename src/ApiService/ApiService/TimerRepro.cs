using Microsoft.Azure.Functions.Worker;

namespace Microsoft.OneFuzz.Service;

public class TimerRepro {
    private readonly ILogTracer _log;

    private readonly IOnefuzzContext _onefuzzContext;

    public TimerRepro(ILogTracer log, IOnefuzzContext onefuzzContext) {
        _log = log;
        _onefuzzContext = onefuzzContext;
    }

    // [Function("TimerRepro")]
    public async Async.Task Run([TimerTrigger("00:00:30")] TimerInfo myTimer) {
        var expired = _onefuzzContext.ReproOperations.SearchExpired();
        await foreach (var repro in expired) {
            _log.Info($"stopping repro: {repro.VmId}");
            await _onefuzzContext.ReproOperations.Stopping(repro);
        }

        var expiredVmIds = expired.Select(repro => repro?.VmId);

        await foreach (var repro in _onefuzzContext.ReproOperations.SearchStates(VmStateHelper.NeedsWork)) {
            if (await expiredVmIds.ContainsAsync(repro.VmId)) {
                // this VM already got processed during the expired phase
                continue;
            }
            _log.Info($"update repro: {repro.VmId}");
            await _onefuzzContext.ReproOperations.ProcessStateUpdates(repro);
        }
    }

}
