using System.Collections.Generic;
using Microsoft.OneFuzz.Service;

namespace IntegrationTests.Fakes;

public sealed class TestMetrics : Metrics {

    public List<BaseMetric> Metrics { get; } = new();
    public List<MetricMessage> CustomMetrics { get; } = new();
    public TestMetrics(ILogTracer log, IOnefuzzContext context)
        : base(log, context) { }
}
