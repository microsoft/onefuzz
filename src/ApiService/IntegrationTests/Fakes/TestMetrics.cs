using System.Collections.Generic;
using Microsoft.OneFuzz.Service;

namespace IntegrationTests.Fakes;

public sealed class TestMetrics : Metrics {

    public List<BaseEvent> Metrics { get; } = new();
    public List<EventMessage> CustomMetrics { get; } = new();
    public TestMetrics(ILogTracer log, IOnefuzzContext context)
        : base(log, context) { }
}
