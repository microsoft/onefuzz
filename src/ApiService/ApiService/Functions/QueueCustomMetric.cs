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

        // var jsonName = "name";
        // var jsonValue = "value";
        // var jsonCustomDimensions = "custom_dimensions";

        // if (!customMetricMessage.RootElement.TryGetProperty(jsonName, out var metricName)) {
        //     _log.WithTag("queueMessage", msg)
        //         .Info($"Expected customMetricMessage to contain a property named '{jsonName}'");
        //     return;
        // }

        // if (!customMetricMessage.RootElement.TryGetProperty(jsonValue, out var metricValue)) {
        //     _log.WithTag("queueMessage", msg)
        //         .Info($"Expected customMetricMessage to contain a property named '{jsonValue}'");
        //     return;
        // }

        // if (!customMetricMessage.RootElement.TryGetProperty(jsonCustomDimensions, out var metricCustomDimensions)) {
        //     _log.WithTag("queueMessage", msg)
        //         .Info($"Expected customMetricMessage to contain a property named '{jsonCustomDimensions}'");
        //     return;
        // }

        // _log.Metric($"{metricName}", int.Parse($"{metricValue}"), metricCustomDimensions);
        _log.Metric($"{customMetricMessage.name}", customMetricMessage.value, customMetricMessage.customDimensions);

    }
}

