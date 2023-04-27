using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service {

    public record CustomMetric
    (
        string Name,
        int Value,
        DateTime CreatedAt,
        BaseMetric CustomDimensions
    );


    public interface IMetrics {
        Async.Task SendMetric(int metricValue, BaseMetric customDimensions);

        void LogMetric(BaseMetric metric);
    }

    public class Metrics : IMetrics {
        private readonly IQueue _queue;
        private readonly ILogTracer _log;
        private readonly JsonSerializerOptions _options;

        public Metrics(IQueue queue, ILogTracer log, IContainers containers, ICreds creds) {
            _queue = queue;
            _log = log;
            _options = new JsonSerializerOptions(EntityConverter.GetJsonSerializerOptions()) {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            _options.Converters.Add(new RemoveUserInfo());
        }

        public async Async.Task QueueCustomMetric(MetricMessage message) {
            await _queue.SendMessage("custom-metrics", JsonSerializer.Serialize(message, _options), StorageType.Config);
        }

        public async Async.Task SendMetric(int metricValue, BaseMetric customDimensions) {
            var metricType = customDimensions.GetMetricType();

            var metricMessage = new MetricMessage(
                metricType,
                customDimensions,
                metricValue
            );
            await QueueCustomMetric(metricMessage);
            LogMetric(customDimensions);
        }

        public void LogMetric(BaseMetric metric) {
            var serializedMetric = JsonSerializer.Serialize(metric, metric.GetType(), _options);
            _log.Info($"sending metric: {metric.GetMetricType():Tag:MetricType} - {serializedMetric}");
        }
    }
}
