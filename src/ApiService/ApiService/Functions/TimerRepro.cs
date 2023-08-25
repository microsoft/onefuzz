using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service.Functions;

public class TimerRepro {
    private readonly ILogger _log;

    private readonly IOnefuzzContext _onefuzzContext;

    public TimerRepro(ILogger<TimerRepro> log, IOnefuzzContext onefuzzContext) {
        _log = log;
        _onefuzzContext = onefuzzContext;
    }

    [Function("TimerRepro")]
    public async Async.Task Run([TimerTrigger("00:00:30")] TimerInfo myTimer) {
        var expiredVmIds = new HashSet<Guid>();
        {
            var expired = _onefuzzContext.ReproOperations.SearchExpired();
            await foreach (var repro in expired) {
                _log.LogInformation("stopping repro: {VmId}", repro.VmId);
                _ = expiredVmIds.Add(repro.VmId);
                // ignoring result: value not used later
                _ = await _onefuzzContext.ReproOperations.Stopping(repro);
            }
        }

        await foreach (var repro in _onefuzzContext.ReproOperations.SearchStates(VmStateHelper.NeedsWork)) {
            if (expiredVmIds.Contains(repro.VmId)) {
                // this VM already got processed during the expired phase
                continue;
            }

            _log.LogInformation("update repro: {VmId}", repro.VmId);
            // ignoring result: value not used later
            _ = await _onefuzzContext.ReproOperations.ProcessStateUpdates(repro);
        }
    }

}
