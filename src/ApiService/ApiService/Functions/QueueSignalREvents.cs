using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service.Functions;

public class QueueSignalREvents {
    private readonly ILogger _logger;

    public QueueSignalREvents(ILogger<QueueSignalREvents> logger) {
        _logger = logger;
    }

    [Function("QueueSignalREvents")]
    [SignalROutput(HubName = "dashboard")]
    public static string Run(
        [QueueTrigger("signalr-events", Connection = "AzureWebJobsStorage")] string msg) {
        return msg;
    }
}
