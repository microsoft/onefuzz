using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.DataContracts;

using System.Diagnostics;

namespace Microsoft.OneFuzz.Service;

public interface ILog {
    void Log(Guid correlationId, String message, SeverityLevel level, IDictionary<string, string> tags, string? caller);
    void LogEvent(Guid correlationId, String evt, IDictionary<string, string> tags, IDictionary<string, double>? metrics, string? caller);
    void LogException(Guid correlationId, Exception ex, IDictionary<string, string> tags, IDictionary<string, double>? metrics, string? caller);
    void Flush();
}

class AppInsights : ILog {
    private TelemetryClient telemetryClient =
            new TelemetryClient(
                new TelemetryConfiguration(EnvironmentVariables.AppInsights.InstrumentationKey));

    public void Log(Guid correlationId, String message, SeverityLevel level, IDictionary<string, string> tags, string? caller) {
        tags.Add("Correlation ID", correlationId.ToString());
        if (caller is not null) tags.Add("CalledBy", caller);
        telemetryClient.TrackTrace(message, level, tags);
    }
    public void LogEvent(Guid correlationId, String evt, IDictionary<string, string> tags, IDictionary<string, double>? metrics, string? caller) {
        tags.Add("Correlation ID", correlationId.ToString());
        if (caller is not null) tags.Add("CalledBy", caller);
        telemetryClient.TrackEvent(evt, properties: tags, metrics: metrics);
    }
    public void LogException(Guid correlationId, Exception ex, IDictionary<string, string> tags, IDictionary<string, double>? metrics, string? caller) {
        tags.Add("Correlation ID", correlationId.ToString());

        if (caller is not null) tags.Add("CalledBy", caller);
        telemetryClient.TrackException(ex, tags, metrics);
    }

    public void Flush() {
        telemetryClient.Flush();
    }
}

//TODO: Should we write errors and Exception to std err ? 
class Console : ILog {

    private string DictToString<T>(IDictionary<string, T>? d) {
        if (d is null)
        {
            return string.Empty;
        }
        else
        {
            return string.Join("", d);
        }
    }

    private void LogTags(Guid correlationId, string? caller, IDictionary<string, string> tags) {
        var ts = DictToString(tags);
        if (!string.IsNullOrEmpty(ts))
        {
            System.Console.WriteLine("[{0}:{1}] Tags:{2}", correlationId, caller, ts);
        }
    }

    private void LogMetrics(Guid correlationId, string? caller, IDictionary<string, double>? metrics) {
        var ms = DictToString(metrics);
        if (!string.IsNullOrEmpty(ms)) {
            System.Console.Out.WriteLine("[{0}:{1}] Metrics:{2}", correlationId, caller, DictToString(metrics));
        }
    }

    public void Log(Guid correlationId, String message, SeverityLevel level, IDictionary<string, string> tags, string? caller) {
        System.Console.Out.WriteLine("[{0}:{1}][{2}] {3}", correlationId, caller, level, message);
        LogTags(correlationId, caller, tags);
    }

    public void LogEvent(Guid correlationId, String evt, IDictionary<string, string> tags, IDictionary<string, double>? metrics, string? caller) {
        System.Console.Out.WriteLine("[{0}:{1}][Event] {2}", correlationId, caller, evt);
        LogTags(correlationId, caller, tags);
        LogMetrics(correlationId, caller, metrics);
    }
    public void LogException(Guid correlationId, Exception ex, IDictionary<string, string> tags, IDictionary<string, double>? metrics, string? caller) {
        System.Console.Out.WriteLine("[{0}:{1}][Exception] {2}", correlationId, caller, ex);
        LogTags(correlationId, caller, tags);
        LogMetrics(correlationId, caller, metrics);
    }
    public void Flush() {
        System.Console.Out.Flush();
    }
}

public class LogTracer {

    private List<ILog> loggers;

    private IDictionary<string, string> tags = new Dictionary<string, string>();
    private Guid correlationId;

    public LogTracer(Guid correlationId, List<ILog> loggers) {
        this.correlationId = correlationId;
        this.loggers = loggers;
    }

    public IDictionary<string, string> Tags => tags;

    public void Info(string message) {
        var caller = new StackTrace()?.GetFrame(1)?.GetMethod()?.Name;
        foreach (var logger in loggers) {
            logger.Log(correlationId, message, SeverityLevel.Information, Tags, caller);
        }
    }

    public void Warning(string message) {
        var caller = new StackTrace()?.GetFrame(1)?.GetMethod()?.Name;
        foreach (var logger in loggers) {
            logger.Log(correlationId, message, SeverityLevel.Warning, Tags, caller);
        }
    }

    public void Error(string message)
    {
        var caller = new StackTrace()?.GetFrame(1)?.GetMethod()?.Name;
        foreach (var logger in loggers)
        {
            logger.Log(correlationId, message, SeverityLevel.Error, Tags, caller);
        }
    }

    public void Critical(string message)
    {
        var caller = new StackTrace()?.GetFrame(1)?.GetMethod()?.Name;
        foreach (var logger in loggers)
        {
            logger.Log(correlationId, message, SeverityLevel.Critical, Tags, caller);
        }
    }

    public void Event(string evt, IDictionary<string, double>? metrics) {
        var caller = new StackTrace()?.GetFrame(1)?.GetMethod()?.Name;
        foreach (var logger in loggers) {
            logger.LogEvent(correlationId, evt, Tags, metrics, caller);
        }
    }

    public void Exception(Exception ex, IDictionary<string, double>? metrics) {
        var caller = new StackTrace()?.GetFrame(1)?.GetMethod()?.Name;
        foreach (var logger in loggers) {
            logger.LogException(correlationId, ex, Tags, metrics, caller);
        }
    }

    public void ForceFlush() {
        foreach (var logger in loggers) {
            logger.Flush();
        }
    }
}

public class LogTracerFactory {

    private List<ILog> loggers;

    public LogTracerFactory(List<ILog> loggers) {
        this.loggers = loggers;
    }

    public LogTracer MakeLogTracer(Guid correlationId) {
        return new LogTracer(correlationId, this.loggers);
    }

}
