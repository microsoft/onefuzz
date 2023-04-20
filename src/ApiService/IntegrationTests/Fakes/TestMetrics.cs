using System.Collections.Generic;
using Microsoft.OneFuzz.Service;

using Async = System.Threading.Tasks;

namespace IntegrationTests.Fakes;

public sealed class TestMetrics : IMetrics {

    public List<BaseMetric> Metrics { get; } = new();
    public List<MetricMessage> CustomMetrics { get; } = new();
    public void LogMetric(BaseMetric metric) {
        Metrics.Add(metric);
    }

    public Async.Task QueueCustomMetric(MetricMessage message) {
        CustomMetrics.Add(message);
        return Async.Task.CompletedTask;
    }

    public Async.Task SendMetric(int metricValue, BaseMetric customDimensions) {
        Metrics.Add(customDimensions);
        return Async.Task.CompletedTask;
    }
}
