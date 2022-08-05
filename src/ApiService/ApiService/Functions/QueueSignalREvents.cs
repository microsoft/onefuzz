using Microsoft.Azure.Functions.Worker;

namespace Microsoft.OneFuzz.Service.Functions;

public class QueueSignalREvents {
    private readonly ILogTracerFactory _loggerFactory;

    public QueueSignalREvents(ILogTracerFactory loggerFactory) {
        _loggerFactory = loggerFactory;
    }

    [Function("QueueSignalREvents")]
    [SignalROutput(HubName = "dashboard")]
    public static string Run(
        [QueueTrigger("signalr-events", Connection = "AzureWebJobsStorage")] string msg) {
        return msg;
    }
}
