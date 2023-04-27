using System.Collections.Generic;
using Microsoft.OneFuzz.Service;

using Async = System.Threading.Tasks;

namespace IntegrationTests.Fakes;

public sealed class TestMetrics : IMetrics {

    public List<BaseMetric> Metrics { get; } = new();
    public List<MetricMessage> CustomMetrics { get; } = new();
    public TestMetrics(ILogTracer log, IOnefuzzContext context)
        : base(log, context) { }
    public void LogMetric(BaseMetric metric) {
        Metrics.Add(metric);
    }
    public Async.Task SendMetric(int metricValue, BaseMetric customDimensions) {
        Metrics.Add(customDimensions);
        return Async.Task.CompletedTask;
    }
}
