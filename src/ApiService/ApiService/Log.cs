using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.DataContracts;

using System.Diagnostics;

namespace Microsoft.OneFuzz.Service;

public interface ILog
{
    void Log(Guid correlationId, String message, SeverityLevel level, IReadOnlyDictionary<string, string> tags, string? caller);
    void LogEvent(Guid correlationId, String evt, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller);
    void LogException(Guid correlationId, Exception ex, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller);
    void Flush();
}

class AppInsights : ILog
{
    private TelemetryClient telemetryClient =
            new TelemetryClient(
                new TelemetryConfiguration(EnvironmentVariables.AppInsights.InstrumentationKey));

    public void Log(Guid correlationId, String message, SeverityLevel level, IReadOnlyDictionary<string, string> tags, string? caller)
    {
        Dictionary<string, string> copyTags = new(tags);
        copyTags["Correlation ID"] = correlationId.ToString();
        if (caller is not null) copyTags["CalledBy"] = caller;
        telemetryClient.TrackTrace(message, level, copyTags);
    }
    public void LogEvent(Guid correlationId, String evt, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller)
    {
        Dictionary<string, string> copyTags = new(tags);
        copyTags["Correlation ID"] = correlationId.ToString();
        if (caller is not null) copyTags["CalledBy"] = caller;

        Dictionary<string, double>? copyMetrics = null;
        if (metrics is not null)
        {
            copyMetrics = new(metrics);
        }

        telemetryClient.TrackEvent(evt, properties: copyTags, metrics: copyMetrics);
    }
    public void LogException(Guid correlationId, Exception ex, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller)
    {
        Dictionary<string, string> copyTags = new(tags);
        copyTags["Correlation ID"] = correlationId.ToString();
        if (caller is not null) copyTags["CalledBy"] = caller;

        Dictionary<string, double>? copyMetrics = null;
        if (metrics is not null)
        {
            copyMetrics = new(metrics);
        }
        telemetryClient.TrackException(ex, copyTags, copyMetrics);
    }

    public void Flush()
    {
        telemetryClient.Flush();
    }
}

//TODO: Should we write errors and Exception to std err ? 
class Console : ILog
{

    private string DictToString<T>(IReadOnlyDictionary<string, T>? d)
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

    private void LogTags(Guid correlationId, IReadOnlyDictionary<string, string> tags)
    {
        var ts = DictToString(tags);
        if (!string.IsNullOrEmpty(ts))
        {
            System.Console.WriteLine($"[{correlationId}] Tags:{ts}");
        }
    }

    private void LogMetrics(Guid correlationId, IReadOnlyDictionary<string, double>? metrics)
    {
        var ms = DictToString(metrics);
        if (!string.IsNullOrEmpty(ms))
        {
            System.Console.Out.WriteLine($"[{correlationId}] Metrics:{DictToString(metrics)}");
        }
    }

    public void Log(Guid correlationId, String message, SeverityLevel level, IReadOnlyDictionary<string, string> tags, string? caller)
    {
        System.Console.Out.WriteLine($"[{correlationId}][{level}] {message}");
        LogTags(correlationId, tags);
    }

    public void LogEvent(Guid correlationId, String evt, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller)
    {
        System.Console.Out.WriteLine($"[{correlationId}][Event] {evt}");
        LogTags(correlationId, tags);
        LogMetrics(correlationId, metrics);
    }
    public void LogException(Guid correlationId, Exception ex, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller)
    {
        System.Console.Out.WriteLine($"[{correlationId}][Exception] {ex}");
        LogTags(correlationId, tags);
        LogMetrics(correlationId, metrics);
    }
    public void Flush()
    {
        System.Console.Out.Flush();
    }
}

public interface ILogTracer
{
    IReadOnlyDictionary<string, string> Tags { get; }

    void Critical(string message);
    void Error(string message);
    void Event(string evt, IReadOnlyDictionary<string, double>? metrics);
    void Exception(Exception ex, IReadOnlyDictionary<string, double>? metrics);
    void ForceFlush();
    void Info(string message);
    void Warning(string message);

    ILogTracer AddTags((string, string)[]? tags);
}

internal interface ILogTracerInternal : ILogTracer
{
    private string? GetCaller()
    {
        return new StackTrace()?.GetFrame(2)?.GetMethod()?.DeclaringType?.FullName;
    }

    private List<ILog> _loggers;

    public Guid CorrelationId { get; }
    public IReadOnlyDictionary<string, string> Tags { get; }


    private static List<KeyValuePair<string, string>> ConvertTags((string, string)[]? tags)
    {
        List<KeyValuePair<string, string>> converted = new List<KeyValuePair<string, string>>();
        if (tags is null)
        {
            return converted;
        }
        else
        {
            foreach (var (k, v) in tags)
            {
                converted.Add(new KeyValuePair<string, string>(k, v));
            }
            return converted;
        }
    }

    public LogTracer(Guid correlationId, (string, string)[]? tags, List<ILog> loggers) : this(correlationId, new Dictionary<string, string>(ConvertTags(tags)), loggers) { }


    public LogTracer(Guid correlationId, IReadOnlyDictionary<string, string> tags, List<ILog> loggers)
    {
        CorrelationId = correlationId;
        Tags = tags;
        _loggers = loggers;
    }

    public ILogTracer AddTags((string, string)[]? tags)
    {
        var newTags = new Dictionary<string, string>(Tags);
        if (tags is not null)
        {
            foreach (var (k, v) in tags)
            {
                newTags[k] = v;
            }
        }
        return new LogTracer(CorrelationId, newTags, _loggers);
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

    public void Event(string evt, IReadOnlyDictionary<string, double>? metrics)
    {
        var caller = GetCaller();
        foreach (var logger in _loggers)
        {
            logger.LogEvent(CorrelationId, evt, Tags, metrics, caller);
        }
    }

    public void Exception(Exception ex, IReadOnlyDictionary<string, double>? metrics)
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
    LogTracer MakeLogTracer(Guid correlationId, (string, string)[]? tags = null);
}

public class LogTracerFactory : ILogTracerFactory
{
    private List<ILog> _loggers;

    public LogTracerFactory(List<ILog> loggers)
    {
        _loggers = loggers;
    }

    public LogTracer MakeLogTracer(Guid correlationId, (string, string)[]? tags = null)
    {
        return new(correlationId, tags, _loggers);
    }

}
