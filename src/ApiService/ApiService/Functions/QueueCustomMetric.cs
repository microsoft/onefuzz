using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service.Functions;


public class QueueCustomMetric {

    private const string QueueCustomMetricQueueNmae = "custom-metrics";


    private readonly ILogTracer _log;

    private readonly IOnefuzzContext _context;

    public QueueCustomMetric(ILogTracer log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    public record CustomMetric(
        string name,
        int value,
        Dictionary<string, string> customDimensions
    );

    [Function("QueueCustomMetric")]
    public void Run([QueueTrigger("custom-metrics", Connection = "AzureWebJobsStorage")] string msg)
    // public void Run([TimerTrigger("00:00:30")] TimerInfo myTimer) {
    {
        // _log.Metric($"Testing Test-Metric");
        // Desiarilize with CustomMetricType
        var customMetricMessage = JsonSerializer.Deserialize<CustomMetric>(msg, EntityConverter.GetJsonSerializerOptions());

        _ = customMetricMessage ?? throw new ArgumentException("Unable to parse queue trigger as JSON");

        //         if (customMetricMessage.name || customMetricMessage.value
        // || customMetricMessage.customDimensions) {
        //             _log.WithTag("queueMessage", msg)
        //                 .Info($"Expected customMetricMessage to contain a missing property - name, value, or custom dimensions.");
        //             return;
        //         }

        _log.Metric($"{customMetricMessage.name}", customMetricMessage.value, customMetricMessage.customDimensions);

    }
}

