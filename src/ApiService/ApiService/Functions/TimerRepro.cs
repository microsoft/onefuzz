using Microsoft.Azure.Functions.Worker;

namespace Microsoft.OneFuzz.Service.Functions;

public class TimerRepro {
    private readonly ILogTracer _log;

    private readonly IOnefuzzContext _onefuzzContext;

    public TimerRepro(ILogTracer log, IOnefuzzContext onefuzzContext) {
        _log = log;
        _onefuzzContext = onefuzzContext;
    }

    [Function("TimerRepro")]
    public async Async.Task Run([TimerTrigger("00:00:10")] TimerInfo myTimer) {
        var expiredVmIds = new HashSet<Guid>();
        {
            var expired = _onefuzzContext.ReproOperations.SearchExpired();
            await foreach (var repro in expired) {
                _log.Info($"stopping repro: {repro.VmId:Tag:VmId}");
                _ = expiredVmIds.Add(repro.VmId);
                // ignoring result: value not used later
                // _ = await _onefuzzContext.ReproOperations.Stopping(repro);
            }
        }

        await foreach (var repro in _onefuzzContext.ReproOperations.SearchStates(VmStateHelper.NeedsWork)) {
            if (expiredVmIds.Contains(repro.VmId)) {
                // this VM already got processed during the expired phase
                continue;
            }

            _log.Info($"update repro: {repro.VmId:Tag:VmId}");
            // ignoring result: value not used later
            _ = await _onefuzzContext.ReproOperations.ProcessStateUpdates(repro);
        }
    }

}
