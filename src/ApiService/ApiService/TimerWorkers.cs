namespace Microsoft.OneFuzz.Service;

public class TimerWorkers {
    ILogTracer _log;
    IScalesetOperations _scaleSetOps;

    public TimerWorkers(ILogTracer log, IScalesetOperations scaleSetOps) {
        _log = log;
        _scaleSetOps = scaleSetOps;
    }

    void ProcessScaleSets(Scaleset scaleset) {
        _log.Verbose($"checking scaleset for updates: {scaleset.ScalesetId}");

        _scaleSetOps.UpdateConfigs(scaleset);

        //if (_scaleSetOps.Cleanup)

    }


    //public async Async.Task Run([TimerTrigger("00:01:30")] TimerInfo t) {
    // NOTE: Update pools first, such that scalesets impacted by pool updates
    // (such as shutdown or resize) happen during this iteration `timer_worker`
    // rather than the following iteration.




    // NOTE: Nodes, and Scalesets should be processed in a consistent order such
    // during 'pool scale down' operations. This means that pools that are
    // scaling down will more likely remove from the same scalesets over time.
    // By more likely removing from the same scalesets, we are more likely to
    // get to empty scalesets, which can safely be deleted.


    //}


}
