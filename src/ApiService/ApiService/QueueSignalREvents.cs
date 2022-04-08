using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Microsoft.OneFuzz.Service;

public class QueueSignalREvents
{
    private readonly ILogger _logger;

    public QueueSignalREvents(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<QueueSignalREvents>();
    }

    [Function("QueueSignalREvents")]
    [SignalROutput(HubName = "dashboard")]
    public static string Run(
        [QueueTrigger("signalr-events-refactored", Connection = "AzureWebJobsStorage")] string msg)
    {
        return msg;
    }
}
