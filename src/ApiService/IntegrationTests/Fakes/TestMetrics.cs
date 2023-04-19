using System.Collections.Generic;
using Microsoft.OneFuzz.Service;

using Async = System.Threading.Tasks;

namespace IntegrationTests.Fakes;

public sealed class TestMetrics : IMetrics {

    public List<BaseMetric> Metrics { get; } = new();
    public List<MetricMessage> CustomMetrics { get; } = new();
    public void LogMetric(BaseMetric anMetric) {
        Metrics.Add(anMetric);
    }

    public Async.Task QueueCustomMetric(MetricMessage message) {
        CustomMetrics.Add(message);
        return Async.Task.CompletedTask;
    }

    public Async.Task SendMetric(BaseMetric anMetric) {
        Metrics.Add(anMetric);
        return Async.Task.CompletedTask;
    }
}
