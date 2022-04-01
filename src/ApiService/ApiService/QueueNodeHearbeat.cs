using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Text.Json;
using Azure.Data.Tables;
using System.Threading.Tasks;
using Azure;


namespace ApiService
{

   

    enum HeartbeatType
    {
        MachineAlive,
        TaskAlive,
    }

    record NodeHeartbeatEntry(string NodeId, Dictionary<string, HeartbeatType>[] data);


    public class QueueNodeHearbeat
    {

        private (string, string) GetStorageAccountNameAndKey(string? accountId)
        {
            //TableEntity
            return ("test", "test");
        }

        private async Task<TableServiceClient> GetStorageClient(string? table, string? accounId)
        {
            accounId ??= System.Environment.GetEnvironmentVariable("ONEFUZZ_FUNC_STORAGE");
            if (accounId == null)
            {
                throw new Exception("ONEFUZZ_FUNC_STORAGE environment variable not set");
            }
            var (name, key) = GetStorageAccountNameAndKey(accounId);
            var tableClient = new TableServiceClient(new Uri(accounId), new TableSharedKeyCredential(name, key));
            await tableClient.CreateTableIfNotExistsAsync(table);
            return tableClient;
        }

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
}





