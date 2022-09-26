using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Microsoft.OneFuzz.Service;

//See: https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/tutorials/interpolated-string-handler
//by ref struct works, but Moq does not support by-ref struct value so all the tests break... https://github.com/moq/moq4/issues/829
[InterpolatedStringHandler]
public struct LogStringHandler {

    private StringBuilder _builder;
    private Dictionary<string, string>? _tags;

    public LogStringHandler(int literalLength, int formattedCount) {
        _builder = new StringBuilder(literalLength);
        _tags = null;
    }

    public void AppendLiteral(string message) {
        _ = _builder.Append(message);
    }

    public void AppendFormatted<T>(T message) {
        _ = _builder.Append(message?.ToString());
    }

    public void AppendFormatted<T>(T message, string? format) {
        if (format is not null && format.StartsWith("Tag:")) {
            var tag = format["Tag:".Length..];
            if (_tags is null) {
                _tags = new Dictionary<string, string>();
            }
            _tags[tag] = $"{message}";
            _ = _builder.Append('{').Append(tag).Append('}');
        } else if (message is IFormattable msg) {
            _ = _builder.Append(msg?.ToString(format, null));
        } else {
            _ = _builder.Append(message?.ToString()).Append(':').Append(format);
        }
    }

    public override string ToString() => _builder.ToString();
    public IReadOnlyDictionary<string, string>? Tags => _tags;
}


public interface ILog {
    void Log(Guid correlationId, LogStringHandler message, SeverityLevel level, IReadOnlyDictionary<string, string> tags, string? caller);
    void LogEvent(Guid correlationId, LogStringHandler evt, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller);
    void LogException(Guid correlationId, Exception ex, LogStringHandler message, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller);
    void Flush();
}

class AppInsights : ILog {
    private readonly TelemetryClient _telemetryClient;

    public AppInsights(TelemetryConfiguration config) {
        _telemetryClient = new TelemetryClient(config);
    }

    public void Log(Guid correlationId, LogStringHandler message, SeverityLevel level, IReadOnlyDictionary<string, string> tags, string? caller) {
        Dictionary<string, string> copyTags = new(tags);
        copyTags["CorrelationId"] = correlationId.ToString();
        if (message.Tags is not null) {
            foreach (var kv in message.Tags) {
                copyTags[kv.Key] = kv.Value;
            }
        }

        if (caller is not null) copyTags["CalledBy"] = caller;
        _telemetryClient.TrackTrace(message.ToString(), level, copyTags);
    }

    public void LogEvent(Guid correlationId, LogStringHandler evt, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller) {
        Dictionary<string, string> copyTags = new(tags);
        copyTags["CorrelationId"] = correlationId.ToString();
        if (caller is not null) copyTags["CalledBy"] = caller;

        if (evt.Tags is not null) {
            foreach (var kv in evt.Tags) {
                copyTags[kv.Key] = kv.Value;
            }
        }

        Dictionary<string, double>? copyMetrics = null;
        if (metrics is not null) {
            copyMetrics = new(metrics);
        }

        _telemetryClient.TrackEvent(evt.ToString(), properties: copyTags, metrics: copyMetrics);
    }

    public void LogException(Guid correlationId, Exception ex, LogStringHandler message, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller) {
        Dictionary<string, string> copyTags = new(tags);
        copyTags["CorrelationId"] = correlationId.ToString();
        if (caller is not null) copyTags["CalledBy"] = caller;

        if (message.Tags is not null) {
            foreach (var kv in message.Tags) {
                copyTags[kv.Key] = kv.Value;
            }
        }


        Dictionary<string, double>? copyMetrics = null;
        if (metrics is not null) {
            copyMetrics = new(metrics);
        }
        _telemetryClient.TrackException(ex, copyTags, copyMetrics);

        Log(correlationId, $"{message} : {ex.Message}", SeverityLevel.Error, tags, caller);
    }

    public void Flush() {
        _telemetryClient.Flush();
    }
}

//TODO: Should we write errors and Exception to std err ? 
class Console : ILog {

    private static string DictToString<T>(IReadOnlyDictionary<string, T>? d) {
        if (d is null) {
            return string.Empty;
        } else {
            return string.Join("", d);
        }
    }

    private static void LogTags(Guid correlationId, IReadOnlyDictionary<string, string> tags) {
        var ts = DictToString(tags);
        if (!string.IsNullOrEmpty(ts)) {
            System.Console.WriteLine($"[{correlationId}] Tags:{ts}");
        }
    }

    private static void LogMetrics(Guid correlationId, IReadOnlyDictionary<string, double>? metrics) {
        var ms = DictToString(metrics);
        if (!string.IsNullOrEmpty(ms)) {
            System.Console.Out.WriteLine($"[{correlationId}] Metrics:{DictToString(metrics)}");
        }
    }

    public void Log(Guid correlationId, LogStringHandler message, SeverityLevel level, IReadOnlyDictionary<string, string> tags, string? caller) {
        System.Console.Out.WriteLine($"[{correlationId}][{level}] {message.ToString()}");
        LogTags(correlationId, tags);
        if (message.Tags is not null) {
            LogTags(correlationId, message.Tags);
        }
    }

    public void LogEvent(Guid correlationId, LogStringHandler evt, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller) {
        System.Console.Out.WriteLine($"[{correlationId}][Event] {evt}");
        LogTags(correlationId, tags);
        LogMetrics(correlationId, metrics);
    }
    public void LogException(Guid correlationId, Exception ex, LogStringHandler message, IReadOnlyDictionary<string, string> tags, IReadOnlyDictionary<string, double>? metrics, string? caller) {
        System.Console.Out.WriteLine($"[{correlationId}][Exception] {message}:{ex}");
        LogTags(correlationId, tags);
        LogMetrics(correlationId, metrics);
    }
    public void Flush() {
        System.Console.Out.Flush();
    }
}

public interface ILogTracer {
    IReadOnlyDictionary<string, string> Tags { get; }

    void Critical(LogStringHandler message);
    void Error(LogStringHandler message);

    void Error(Error error);
    void Event(LogStringHandler evt, IReadOnlyDictionary<string, double>? metrics);
    void Exception(Exception ex, LogStringHandler message = $"", IReadOnlyDictionary<string, double>? metrics = null);
    void ForceFlush();
    void Info(LogStringHandler message);
    void Warning(LogStringHandler message);
    void Verbose(LogStringHandler message);

    ILogTracer WithTag(string k, string v);
    ILogTracer WithTags(IEnumerable<(string, string)>? tags);
    ILogTracer WithHttpStatus((int, string) status);
}

internal interface ILogTracerInternal : ILogTracer {
    void ReplaceCorrelationId(Guid newCorrelationId);
    void AddTags(IEnumerable<(string, string)> tags);
}



public class LogTracer : ILogTracerInternal {
    private static string? GetCaller() {
        return new StackTrace()?.GetFrame(2)?.GetMethod()?.DeclaringType?.FullName;
    }

    private Guid _correlationId;
    private IEnumerable<ILog> _loggers;
    private Dictionary<string, string> _tags;
    private SeverityLevel _logSeverityLevel;

    public Guid CorrelationId => _correlationId;
    public IReadOnlyDictionary<string, string> Tags => _tags;

    private static IEnumerable<KeyValuePair<string, string>> ConvertTags(IEnumerable<(string, string)>? tags) {
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

    public LogTracer(Guid correlationId, IEnumerable<(string, string)>? tags, List<ILog> loggers, SeverityLevel logSeverityLevel) :
        this(correlationId, new Dictionary<string, string>(ConvertTags(tags)), loggers, logSeverityLevel) { }


    public LogTracer(Guid correlationId, IReadOnlyDictionary<string, string> tags, IEnumerable<ILog> loggers, SeverityLevel logSeverityLevel) {
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
    public void AddTags(IEnumerable<(string, string)> tags) {
        if (tags is not null) {
            foreach (var (k, v) in tags) {
                _tags[k] = v;
            }
        }
    }

    public ILogTracer WithTag(string k, string v) {
        return WithTags(new[] { (k, v) });
    }

    public ILogTracer WithHttpStatus((int, string) status) {
        (string, string)[] tags = { ("StatusCode", status.Item1.ToString()), ("ReasonPhrase", status.Item2) };
        return WithTags(tags);
    }

    public ILogTracer WithTags(IEnumerable<(string, string)>? tags) {
        var newTags = new Dictionary<string, string>(Tags);
        if (tags is not null) {
            foreach (var (k, v) in tags) {
                newTags[k] = v;
            }
        }
        return new LogTracer(CorrelationId, newTags, _loggers, _logSeverityLevel);
    }

    public void Verbose(LogStringHandler message) {
        if (_logSeverityLevel <= SeverityLevel.Verbose) {
            var caller = GetCaller();
            foreach (var logger in _loggers) {
                logger.Log(CorrelationId, message, SeverityLevel.Verbose, Tags, caller);
            }
        }
    }

    public void Info(LogStringHandler message) {
        if (_logSeverityLevel <= SeverityLevel.Information) {
            var caller = GetCaller();
            foreach (var logger in _loggers) {
                logger.Log(CorrelationId, message, SeverityLevel.Information, Tags, caller);
            }
        }
    }

    public void Warning(LogStringHandler message) {
        if (_logSeverityLevel <= SeverityLevel.Warning) {
            var caller = GetCaller();
            foreach (var logger in _loggers) {
                logger.Log(CorrelationId, message, SeverityLevel.Warning, Tags, caller);
            }
        }
    }

    public void Error(LogStringHandler message) {
        if (_logSeverityLevel <= SeverityLevel.Error) {
            var caller = GetCaller();
            foreach (var logger in _loggers) {
                logger.Log(CorrelationId, message, SeverityLevel.Error, Tags, caller);
            }
        }
    }

    public void Critical(LogStringHandler message) {
        if (_logSeverityLevel <= SeverityLevel.Critical) {
            var caller = GetCaller();
            foreach (var logger in _loggers) {
                logger.Log(CorrelationId, message, SeverityLevel.Critical, Tags, caller);
            }
        }
    }

    public void Event(LogStringHandler evt, IReadOnlyDictionary<string, double>? metrics) {
        var caller = GetCaller();
        foreach (var logger in _loggers) {
            logger.LogEvent(CorrelationId, evt, Tags, metrics, caller);
        }
    }

    public void Exception(Exception ex, LogStringHandler message, IReadOnlyDictionary<string, double>? metrics) {
        var caller = GetCaller();
        foreach (var logger in _loggers) {
            logger.LogException(CorrelationId, ex, message, Tags, metrics, caller);
        }
    }

    public void ForceFlush() {
        foreach (var logger in _loggers) {
            logger.Flush();
        }
    }

    public void Error(Error error) {
        Error($"{error:Tag:Error}");
    }
}

public interface ILogTracerFactory {
    LogTracer CreateLogTracer(Guid correlationId, IEnumerable<(string, string)>? tags = null, SeverityLevel severityLevel = SeverityLevel.Verbose);
}

public class LogTracerFactory : ILogTracerFactory {
    private List<ILog> _loggers;

    public LogTracerFactory(List<ILog> loggers) {
        _loggers = loggers;
    }

    public LogTracer CreateLogTracer(Guid correlationId, IEnumerable<(string, string)>? tags = null, SeverityLevel severityLevel = SeverityLevel.Verbose) {
        return new(correlationId, tags, _loggers, severityLevel);
    }

}

public interface ILogSinks {
    List<ILog> GetLogSinks();
}

public class LogSinks : ILogSinks {
    private readonly List<ILog> _loggers;

    public LogSinks(IServiceConfig config, TelemetryConfiguration telemetryConfiguration) {
        _loggers = new List<ILog>();
        foreach (var dest in config.LogDestinations) {
            _loggers.Add(
                dest switch {
                    LogDestination.AppInsights => new AppInsights(telemetryConfiguration),
                    LogDestination.Console => new Console(),
                    _ => throw new Exception($"Unhandled Log Destination type: {dest}"),
                }
            );
        }
    }
    public List<ILog> GetLogSinks() {
        return _loggers;
    }
}
