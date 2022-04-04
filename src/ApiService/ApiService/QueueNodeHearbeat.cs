using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Text.Json;
using Azure.Data.Tables;
using System.Threading.Tasks;
using Azure;

namespace Microsoft.OneFuzz.Service;


enum HeartbeatType
{
    MachineAlive,
    TaskAlive,
}

record NodeHeartbeatEntry(string NodeId, Dictionary<string, HeartbeatType>[] data);


public class QueueNodeHearbeat
{

    private readonly ILogger _logger;

    public QueueNodeHearbeat(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<QueueNodeHearbeat>();
    }

    [Function("QueueNodeHearbeat")]
    public void Run([QueueTrigger("myqueue-items", Connection = "AzureWebJobsStorage")] string msg)
    {
        var hb = JsonSerializer.Deserialize<NodeHeartbeatEntry>(msg);
        


        _logger.LogInformation($"heartbeat: {msg}");
    }
}





