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
        Async.Task SendMetric(BaseMetric anMetric);

        void LogMetric(BaseMetric anMetric);
    }

    public class Metrics : IMetrics {
        private readonly IQueue _queue;
        private readonly ILogTracer _log;
        private readonly IContainers _containers;
        private readonly ICreds _creds;
        private readonly JsonSerializerOptions _options;

        public Metrics(IQueue queue, IWebhookOperations webhook, ILogTracer log, IContainers containers, ICreds creds) {
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

        public async Async.Task SendMetric(BaseMetric anMetric) {
            var metricType = anMetric.GetMetricType();

            var instanceId = await _containers.GetInstanceId();

            // var value = anMetric.metricValue;
            var customDimensions = anMetric;

            var metricMessage = new MetricMessage(
                Guid.NewGuid(),
                metricType,
                anMetric, // customDimensions? 
                instanceId,
                _creds.GetInstanceName()
            );
            await QueueCustomMetric(metricMessage);
            LogMetric(anMetric);
        }

        public void LogMetric(BaseMetric anMetric) {
            var serializedMetric = JsonSerializer.Serialize(anMetric, anMetric.GetType(), _options);
            _log.Info($"sending metric: {anMetric.GetMetricType():Tag:MetricType} - {serializedMetric}");
        }
    }
}
