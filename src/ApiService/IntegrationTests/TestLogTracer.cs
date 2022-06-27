using System;
using System.Collections.Generic;
using Microsoft.OneFuzz.Service;
using Xunit.Abstractions;

namespace IntegrationTests;

sealed class TestLogTracer : ILogTracer {
    private readonly ITestOutputHelper _output;

    public TestLogTracer(ITestOutputHelper output)
        => _output = output;

    private readonly Dictionary<string, string> _tags = new();
    public IReadOnlyDictionary<string, string> Tags => _tags;

    public void Critical(string message) {
        _output.WriteLine($"[Critical] {message}");
    }

    public void Error(string message) {
        _output.WriteLine($"[Error] {message}");
    }

    public void Event(string evt, IReadOnlyDictionary<string, double>? metrics) {
        // TODO: metrics
        _output.WriteLine($"[Event] [{evt}]");
    }

    public void Exception(Exception ex, string message = "", IReadOnlyDictionary<string, double>? metrics = null) {
        // TODO: metrics
        _output.WriteLine($"[Error] {message} {ex}");
    }

    public void ForceFlush() {
        // nothing to do
    }

    public void Info(string message) {
        _output.WriteLine($"[Info] {message}");
    }

    public void Verbose(string message) {
        _output.WriteLine($"[Verbose] {message}");
    }

    public void Warning(string message) {
        _output.WriteLine($"[Warning] {message}");
    }

    public ILogTracer WithHttpStatus((int, string) status) {
        return this; // TODO?
    }

    public ILogTracer WithTag(string k, string v) {
        return this; // TODO?
    }

    public ILogTracer WithTags(IEnumerable<(string, string)>? tags) {
        return this; // TODO?
    }
}
