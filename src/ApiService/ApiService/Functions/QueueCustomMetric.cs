// using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
// using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service.Functions;


public class QueueCustomMetric {
    private readonly ILogTracer _log;

    private readonly IOnefuzzContext _context;

    public QueueCustomMetric(ILogTracer log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("QueueCustomMetric")]
    // public void Run([QueueTrigger("custom-metrics", Connection = "AzureWebJobsStorage")] string msg)
    public void Run([TimerTrigger("00:00:30")] TimerInfo myTimer) {
        {
            _log.Metric($"Testing Test-Metric");
        }
    }
}
