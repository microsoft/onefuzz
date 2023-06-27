using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
namespace Microsoft.OneFuzz.Service {

    public record CustomMetric(
         string name,
         int value,
         Dictionary<string, string> customDimensions
    );


    public interface IMetrics {
        void SendMetric(int metricValue, BaseEvent customDimensions);

        void LogMetric(BaseEvent metric);
    }

    public class Metrics : IMetrics {
        private readonly ILogger _log;
        private readonly IOnefuzzContext _context;
        private readonly JsonSerializerOptions _options;

        public Metrics(ILogger<Metrics> log, IOnefuzzContext context) {
            _context = context;
            _log = log;
            _options = new JsonSerializerOptions(EntityConverter.GetJsonSerializerOptions()) {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            _options.Converters.Add(new RemoveUserInfo());
        }

        public void SendMetric(int metricValue, BaseEvent customDimensions) {
            var metricType = customDimensions.GetEventType();

            _ = _options.PropertyNamingPolicy ?? throw new ArgumentException("Serializer _options not available.");

            var metricTypeSnakeCase = _options.PropertyNamingPolicy.ConvertName($"{metricType}");

            var dimensionNode = JsonSerializer.SerializeToNode(customDimensions, customDimensions.GetType(), _options);
            _ = dimensionNode ?? throw new JsonException("Was not able to properly serialize the custom dimensions.");
            var dimensionDict = dimensionNode.AsObject().ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value is not null ? kvp.Value.ToString() : "");
            _log.AddTags(dimensionDict);
            _log.LogMetric($"{metricTypeSnakeCase}", metricValue);
            LogMetric(customDimensions);
        }

        public void LogMetric(BaseEvent metric) {
            var serializedMetric = JsonSerializer.Serialize(metric, metric.GetType(), _options);
            _log.LogInformation("sending metric: {MetricType} - {SerializedMetric}", metric.GetEventType(), serializedMetric);
        }
    }
}
