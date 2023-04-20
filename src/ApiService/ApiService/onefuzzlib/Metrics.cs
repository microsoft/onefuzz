using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service {

    // public record CustomDimensions(
    //     string? JobId = null,
    //     string? TaskId = null,
    //     string? ScalesetId = null,
    //     string? NodeId = null,
    //     string? Project = null,
    //     string? Name = null,
    //     string? Build = null,
    //     string? ADOOrganization = null,
    //     string? ADOProject = null,
    //     string? TargetExeName = null,
    //     string? Branch = null
    // );

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
        private readonly IContainers _containers;
        private readonly ICreds _creds;
        private readonly JsonSerializerOptions _options;

        public Metrics(IQueue queue, ILogTracer log, IContainers containers, ICreds creds) {
            _queue = queue;
            _log = log;
            _containers = containers;
            _creds = creds;
            _options = new JsonSerializerOptions(EntityConverter.GetJsonSerializerOptions()) {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            _options.Converters.Add(new RemoveUserInfo());
        }

        public async Async.Task QueueCustomMetric(MetricMessage message) {
            // var customDimensions = new CustomDimensions();
            // var metric = new CustomMetric(
            //     Name: "custom-metric",
            //     Value: 0,
            //     CreatedAt: DateTime.UtcNow,
            //     CustomDimensions: customDimensions
            // );
            await _queue.SendMessage("custom-metrics", JsonSerializer.Serialize(message, _options), StorageType.Config);
        }

        public async Async.Task SendMetric(int metricValue, BaseMetric customDimensions) {
            var metricType = customDimensions.GetMetricType();

            var metricMessage = new MetricMessage(
                metricType,
                customDimensions, // customDimensions? 
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
