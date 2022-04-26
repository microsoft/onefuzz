using System.Diagnostics;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Microsoft.OneFuzz.Service;

public interface ILog {
    void Log(Guid correlationId, String message, SeverityLevel level, IReadOnlyDictionary<string, string> tags, string? caller);
    void LogEvent(Guid correlationId, String evt, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller);
    void LogException(Guid correlationId, Exception ex, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller);
    void Flush();
}

class AppInsights : ILog {
    private TelemetryClient _telemetryClient;

    public AppInsights(string instrumentationKey) {
        _telemetryClient = new TelemetryClient(new TelemetryConfiguration(instrumentationKey));
    }

    public void Log(Guid correlationId, String message, SeverityLevel level, IReadOnlyDictionary<string, string> tags, string? caller) {
        Dictionary<string, string> copyTags = new(tags);
        copyTags["Correlation ID"] = correlationId.ToString();
        if (caller is not null) copyTags["CalledBy"] = caller;
        _telemetryClient.TrackTrace(message, level, copyTags);
    }
    public void LogEvent(Guid correlationId, String evt, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller) {
        Dictionary<string, string> copyTags = new(tags);
        copyTags["Correlation ID"] = correlationId.ToString();
        if (caller is not null) copyTags["CalledBy"] = caller;

        Dictionary<string, double>? copyMetrics = null;
        if (metrics is not null) {
            copyMetrics = new(metrics);
        }

        _telemetryClient.TrackEvent(evt, properties: copyTags, metrics: copyMetrics);
    }
    public void LogException(Guid correlationId, Exception ex, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller) {
        Dictionary<string, string> copyTags = new(tags);
        copyTags["Correlation ID"] = correlationId.ToString();
        if (caller is not null) copyTags["CalledBy"] = caller;

        Dictionary<string, double>? copyMetrics = null;
        if (metrics is not null) {
            copyMetrics = new(metrics);
        }
        _telemetryClient.TrackException(ex, copyTags, copyMetrics);
    }

    public void Flush() {
        _telemetryClient.Flush();
    }
}

//TODO: Should we write errors and Exception to std err ? 
class Console : ILog {

    private string DictToString<T>(IReadOnlyDictionary<string, T>? d) {
        if (d is null) {
            return string.Empty;
        } else {
            return string.Join("", d);
        }
    }

    private void LogTags(Guid correlationId, IReadOnlyDictionary<string, string> tags) {
        var ts = DictToString(tags);
        if (!string.IsNullOrEmpty(ts)) {
            System.Console.WriteLine($"[{correlationId}] Tags:{ts}");
        }
    }

    private void LogMetrics(Guid correlationId, IReadOnlyDictionary<string, double>? metrics) {
        var ms = DictToString(metrics);
        if (!string.IsNullOrEmpty(ms)) {
            System.Console.Out.WriteLine($"[{correlationId}] Metrics:{DictToString(metrics)}");
        }
    }

    public void Log(Guid correlationId, String message, SeverityLevel level, IReadOnlyDictionary<string, string> tags, string? caller) {
        System.Console.Out.WriteLine($"[{correlationId}][{level}] {message}");
        LogTags(correlationId, tags);
    }

    public void LogEvent(Guid correlationId, String evt, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller) {
        System.Console.Out.WriteLine($"[{correlationId}][Event] {evt}");
        LogTags(correlationId, tags);
        LogMetrics(correlationId, metrics);
    }
    public void LogException(Guid correlationId, Exception ex, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller) {
        System.Console.Out.WriteLine($"[{correlationId}][Exception] {ex}");
        LogTags(correlationId, tags);
        LogMetrics(correlationId, metrics);
    }
    public void Flush() {
        System.Console.Out.Flush();
    }
}

public interface ILogTracer {
    IReadOnlyDictionary<string, string> Tags { get; }

    void Critical(string message);
    void Error(string message);
    void Event(string evt, IReadOnlyDictionary<string, double>? metrics);
    void Exception(Exception ex, IReadOnlyDictionary<string, double>? metrics);
    void ForceFlush();
    void Info(string message);
    void Warning(string message);
    void Verbose(string message);

    ILogTracer WithTag(string k, string v);
    ILogTracer WithTags((string, string)[]? tags);
}

internal interface ILogTracerInternal : ILogTracer {
    void ReplaceCorrelationId(Guid newCorrelationId);
    void AddTags((string, string)[] tags);
}



public class LogTracer : ILogTracerInternal {
    private string? GetCaller() {
        return new StackTrace()?.GetFrame(2)?.GetMethod()?.DeclaringType?.FullName;
    }

    private Guid _correlationId;
    private List<ILog> _loggers;
    private Dictionary<string, string> _tags;
    private SeverityLevel _logSeverityLevel;

    public Guid CorrelationId => _correlationId;
    public IReadOnlyDictionary<string, string> Tags => _tags;

    private static List<KeyValuePair<string, string>> ConvertTags((string, string)[]? tags) {
        List<KeyValuePair<string, string>> converted = new List<KeyValuePair<string, string>>();
        if (tags is null) {
            return converted;
        } else {
            foreach (var (k, v) in tags) {
                converted.Add(new KeyValuePair<string, string>(k, v));
            }
            return converted;
        }
    }

    public LogTracer(Guid correlationId, (string, string)[]? tags, List<ILog> loggers, SeverityLevel logSeverityLevel) :
        this(correlationId, new Dictionary<string, string>(ConvertTags(tags)), loggers, logSeverityLevel) { }


    public LogTracer(Guid correlationId, IReadOnlyDictionary<string, string> tags, List<ILog> loggers, SeverityLevel logSeverityLevel) {
        _correlationId = correlationId;
        _tags = new(tags);
        _loggers = loggers;
        _logSeverityLevel = logSeverityLevel;
    }

    //Single threaded only
    public void ReplaceCorrelationId(Guid newCorrelationId) {
        _correlationId = newCorrelationId;
    }

    //single threaded only
    public void AddTags((string, string)[] tags) {
        if (tags is not null) {
            foreach (var (k, v) in tags) {
                _tags[k] = v;
            }
        }
    }

    public ILogTracer WithTag(string k, string v) {
        return WithTags(new[] { (k, v) });
    }

    public ILogTracer WithTags((string, string)[]? tags) {
        var newTags = new Dictionary<string, string>(Tags);
        if (tags is not null) {
            foreach (var (k, v) in tags) {
                newTags[k] = v;
            }
        }
        return new LogTracer(CorrelationId, newTags, _loggers, _logSeverityLevel);
    }

    public void Verbose(string message) {
        if (_logSeverityLevel <= SeverityLevel.Verbose) {
            var caller = GetCaller();
            foreach (var logger in _loggers) {
                logger.Log(CorrelationId, message, SeverityLevel.Verbose, Tags, caller);
            }
        }
    }

    public void Info(string message) {
        if (_logSeverityLevel <= SeverityLevel.Information) {
            var caller = GetCaller();
            foreach (var logger in _loggers) {
                logger.Log(CorrelationId, message, SeverityLevel.Information, Tags, caller);
            }
        }
    }

    public void Warning(string message) {
        if (_logSeverityLevel <= SeverityLevel.Warning) {
            var caller = GetCaller();
            foreach (var logger in _loggers) {
                logger.Log(CorrelationId, message, SeverityLevel.Warning, Tags, caller);
            }
        }
    }

    public void Error(string message) {
        if (_logSeverityLevel <= SeverityLevel.Error) {
            var caller = GetCaller();
            foreach (var logger in _loggers) {
                logger.Log(CorrelationId, message, SeverityLevel.Error, Tags, caller);
            }
        }
    }

    public void Critical(string message) {
        if (_logSeverityLevel <= SeverityLevel.Critical) {
            var caller = GetCaller();
            foreach (var logger in _loggers) {
                logger.Log(CorrelationId, message, SeverityLevel.Critical, Tags, caller);
            }
        }
    }

    public void Event(string evt, IReadOnlyDictionary<string, double>? metrics) {
        var caller = GetCaller();
        foreach (var logger in _loggers) {
            logger.LogEvent(CorrelationId, evt, Tags, metrics, caller);
        }
    }

    public void Exception(Exception ex, IReadOnlyDictionary<string, double>? metrics) {
        var caller = GetCaller();
        foreach (var logger in _loggers) {
            logger.LogException(CorrelationId, ex, Tags, metrics, caller);
        }
    }

    public void ForceFlush() {
        foreach (var logger in _loggers) {
            logger.Flush();
        }
    }
}

public interface ILogTracerFactory {
    LogTracer CreateLogTracer(Guid correlationId, (string, string)[]? tags = null, SeverityLevel severityLevel = SeverityLevel.Verbose);
}

public class LogTracerFactory : ILogTracerFactory {
    private List<ILog> _loggers;

    public LogTracerFactory(List<ILog> loggers) {
        _loggers = loggers;
    }

    public LogTracer CreateLogTracer(Guid correlationId, (string, string)[]? tags = null, SeverityLevel severityLevel = SeverityLevel.Verbose) {
        return new(correlationId, tags, _loggers, severityLevel);
    }

}
