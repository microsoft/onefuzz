using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.OneFuzz.Service;
using Xunit.Abstractions;

namespace IntegrationTests;

sealed class TestLogTracer : ILogTracer {
    private readonly ITestOutputHelper _output;

    public TestLogTracer(ITestOutputHelper output)
        => _output = output;

    private readonly Dictionary<string, string> _tags = new();
    public IReadOnlyDictionary<string, string> Tags => _tags;

    public void Critical(LogStringHandler message) {
        _output.WriteLine($"[Critical] {message.ToString()}");
    }

    public void Error(LogStringHandler message) {
        _output.WriteLine($"[Error] {message.ToString()}");
    }

    public void Event(LogStringHandler evt, IReadOnlyDictionary<string, double>? metrics) {
        // TODO: metrics
        _output.WriteLine($"[Event] [{evt}]");
    }

    public void Metric(LogStringHandler metric, int value, IReadOnlyDictionary<string, string>? customDimensions) {
        // TODO: metrics
        _output.WriteLine($"[Event] [{metric}]");
    }

    public void Exception(Exception ex, LogStringHandler message = $"", IReadOnlyDictionary<string, double>? metrics = null) {
        // TODO: metrics
        _output.WriteLine($"[Error] {message} {ex}");
    }

    public void ForceFlush() {
        // nothing to do
    }

    public void Info(LogStringHandler message) {
        _output.WriteLine($"[Info] {message.ToString()}");
    }

    public void Verbose(LogStringHandler message) {
        _output.WriteLine($"[Verbose] {message.ToString()}");
    }

    public void Warning(LogStringHandler message) {
        _output.WriteLine($"[Warning] {message.ToString()}");
    }

    public ILogTracer WithHttpStatus((HttpStatusCode Status, string Reason) result) {
        return this; // TODO?
    }

    public ILogTracer WithTag(string k, string v) {
        return this; // TODO?
    }

    public ILogTracer WithTags(IEnumerable<(string, string)>? tags) {
        return this; // TODO?
    }

    public void Error(Error error) {
        Error($"{error}");
    }

    public void Warning(Error error) {
        Warning($"{error}");
    }
}
