using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.DataContracts;

using System.Diagnostics;

namespace Microsoft.OneFuzz.Service;

public interface ILog
{
    void Log(Guid correlationId, String message, SeverityLevel level, IDictionary<string, string> tags, string? caller);
    void LogEvent(Guid correlationId, String evt, IDictionary<string, string> tags, IDictionary<string, double>? metrics, string? caller);
    void LogException(Guid correlationId, Exception ex, IDictionary<string, string> tags, IDictionary<string, double>? metrics, string? caller);
    void Flush();
}

class AppInsights : ILog
{
    private TelemetryClient telemetryClient =
            new TelemetryClient(
                new TelemetryConfiguration(EnvironmentVariables.AppInsights.InstrumentationKey));

    public void Log(Guid correlationId, String message, SeverityLevel level, IDictionary<string, string> tags, string? caller)
    {
        tags["Correlation ID"] = correlationId.ToString();
        if (caller is not null) tags["CalledBy"] = caller;
        telemetryClient.TrackTrace(message, level, tags);
    }
    public void LogEvent(Guid correlationId, String evt, IDictionary<string, string> tags, IDictionary<string, double>? metrics, string? caller)
    {
        tags["Correlation ID"] = correlationId.ToString();
        if (caller is not null) tags["CalledBy"] = caller;
        telemetryClient.TrackEvent(evt, properties: tags, metrics: metrics);
    }
    public void LogException(Guid correlationId, Exception ex, IDictionary<string, string> tags, IDictionary<string, double>? metrics, string? caller)
    {
        tags["Correlation ID"] = correlationId.ToString();

        if (caller is not null) tags["CalledBy"] = caller;
        telemetryClient.TrackException(ex, tags, metrics);
    }

    public void Flush()
    {
        telemetryClient.Flush();
    }
}

//TODO: Should we write errors and Exception to std err ? 
class Console : ILog
{

    private string DictToString<T>(IDictionary<string, T>? d)
    {
        if (d is null)
        {
            return string.Empty;
        }
        else
        {
            return string.Join("", d);
        }
    }

    private void LogTags(Guid correlationId, string? caller, IDictionary<string, string> tags)
    {
        var ts = DictToString(tags);
        if (!string.IsNullOrEmpty(ts))
        {
            System.Console.WriteLine($"[{correlationId}:{caller}] Tags:{ts}");
        }
    }

    private void LogMetrics(Guid correlationId, string? caller, IDictionary<string, double>? metrics)
    {
        var ms = DictToString(metrics);
        if (!string.IsNullOrEmpty(ms))
        {
            System.Console.Out.WriteLine($"[{correlationId}:{caller}] Metrics:{DictToString(metrics)}");
        }
    }

    public void Log(Guid correlationId, String message, SeverityLevel level, IDictionary<string, string> tags, string? caller)
    {
        System.Console.Out.WriteLine($"[{correlationId}:{caller}][{level}] {message}");
        LogTags(correlationId, caller, tags);
    }

    public void LogEvent(Guid correlationId, String evt, IDictionary<string, string> tags, IDictionary<string, double>? metrics, string? caller)
    {
        System.Console.Out.WriteLine($"[{correlationId}:{caller}][Event] {evt}");
        LogTags(correlationId, caller, tags);
        LogMetrics(correlationId, caller, metrics);
    }
    public void LogException(Guid correlationId, Exception ex, IDictionary<string, string> tags, IDictionary<string, double>? metrics, string? caller)
    {
        System.Console.Out.WriteLine($"[{correlationId}:{caller}][Exception] {ex}");
        LogTags(correlationId, caller, tags);
        LogMetrics(correlationId, caller, metrics);
    }
    public void Flush()
    {
        System.Console.Out.Flush();
    }
}

public interface ILogTracer
{
    IDictionary<string, string> Tags { get; }

    void Critical(string message);
    void Error(string message);
    void Event(string evt, IDictionary<string, double>? metrics);
    void Exception(Exception ex, IDictionary<string, double>? metrics);
    void ForceFlush();
    void Info(string message);
    void Warning(string message);
}

public class LogTracer : ILogTracer
{
    private string? GetCaller()
    {
        return new StackTrace()?.GetFrame(2)?.GetMethod()?.DeclaringType?.FullName;
    }

    private List<ILog> _loggers;

    public Guid CorrelationId { get; }
    public IDictionary<string, string> Tags { get; }

    public LogTracer(Guid correlationId, List<ILog> loggers)
    {
        CorrelationId = correlationId;
        Tags = new Dictionary<string, string>();
        _loggers = loggers;
    }

    public void Info(string message)
    {
        var caller = GetCaller();
        foreach (var logger in _loggers)
        {
            logger.Log(CorrelationId, message, SeverityLevel.Information, Tags, caller);
        }
    }

    public void Warning(string message)
    {
        var caller = GetCaller();
        foreach (var logger in _loggers)
        {
            logger.Log(CorrelationId, message, SeverityLevel.Warning, Tags, caller);
        }
    }

    public void Error(string message)
    {
        var caller = GetCaller();
        foreach (var logger in _loggers)
        {
            logger.Log(CorrelationId, message, SeverityLevel.Error, Tags, caller);
        }
    }

    public void Critical(string message)
    {
        var caller = GetCaller();
        foreach (var logger in _loggers)
        {
            logger.Log(CorrelationId, message, SeverityLevel.Critical, Tags, caller);
        }
    }

    public void Event(string evt, IDictionary<string, double>? metrics)
    {
        var caller = GetCaller();
        foreach (var logger in _loggers)
        {
            logger.LogEvent(CorrelationId, evt, Tags, metrics, caller);
        }
    }

    public void Exception(Exception ex, IDictionary<string, double>? metrics)
    {
        var caller = GetCaller();
        foreach (var logger in _loggers)
        {
            logger.LogException(CorrelationId, ex, Tags, metrics, caller);
        }
    }

    public void ForceFlush()
    {
        foreach (var logger in _loggers)
        {
            logger.Flush();
        }
    }
}

public interface ILogTracerFactory
{
    LogTracer MakeLogTracer(Guid correlationId);
}

public class LogTracerFactory : ILogTracerFactory
{
    private List<ILog> _loggers;

    public LogTracerFactory(List<ILog> loggers)
    {
        _loggers = loggers;
    }

    public LogTracer MakeLogTracer(Guid correlationId)
    {
        return new(correlationId, _loggers);
    }

}
