using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Azure.Storage.Queues.Models;
using System.Linq;

namespace Microsoft.OneFuzz.Service;

public class QueueSignalREvents {
    private readonly ILogger _logger;

    public QueueSignalREvents(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<QueueSignalREvents>();
    }

    [Function("QueueSignalREvents")]
    [SignalROutput(HubName="dashboard")]
    public static string Run(
        [QueueTrigger("signalr-events-refactored", Connection = "AzureWebJobsStorage")] string msg)
    {
        return msg;
    }
}
