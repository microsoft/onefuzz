using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service {

    public record CustomMetric(
         string name,
         int value,
         Dictionary<string, string> customDimensions
    );


    public interface IMetrics {
        void SendMetric(int metricValue, BaseMetric customDimensions);

        void LogMetric(BaseMetric metric);
    }

    public class Metrics : IMetrics {
        private readonly ILogTracer _log;
        private readonly IOnefuzzContext _context;
        private readonly JsonSerializerOptions _options;

        public Metrics(ILogTracer log, IOnefuzzContext context) {
            _context = context;
            _log = log;
            _options = new JsonSerializerOptions(EntityConverter.GetJsonSerializerOptions()) {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            _options.Converters.Add(new RemoveUserInfo());
        }

        public void SendMetric(int metricValue, BaseMetric customDimensions) {
            var metricType = customDimensions.GetMetricType();

            _ = _options.PropertyNamingPolicy ?? throw new ArgumentException("Serializer _options not available.");

            var metricTypeSnakeCase = _options.PropertyNamingPolicy.ConvertName($"{metricType}");

            var dimensionString = JsonSerializer.Serialize(customDimensions, customDimensions.GetType(), _options);
            var dimensionDict = JsonSerializer.Deserialize<Dictionary<string, string>>(dimensionString);

            _log.Metric($"{metricTypeSnakeCase}", metricValue, dimensionDict);
            LogMetric(customDimensions);
        }

        public void LogMetric(BaseMetric metric) {
            var serializedMetric = JsonSerializer.Serialize(metric, metric.GetType(), _options);
            _log.Info($"sending metric: {metric.GetMetricType():Tag:MetricType} - {serializedMetric}");
        }
    }
}
