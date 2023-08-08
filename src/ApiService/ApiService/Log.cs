using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.OneFuzz.Service;

public enum Telemetry {
    Trace,
    Exception,
    Request,
    Dependency,
    PageView,
    Availability,
    Metric,
    Event
}

/// <param name="TelemetryClient"></param>
/// <param name="EnabledTelemetry"></param>
public record TelemetryConfig(TelemetryClient TelemetryClient, ISet<Telemetry>? EnabledTelemetry = null);


public class OneFuzzLogger : ILogger {

    public const string CorrelationId = "CorrelationId";
    public const string TraceId = "TraceId";
    public const string SpanId = "SpanId";

    private readonly string categoryName;

    private readonly IEnumerable<TelemetryConfig> telemetryConfig;


    /// <param name="categoryName"></param>
    /// <param name="telemetryConfig"></param>
    public OneFuzzLogger(string categoryName, IEnumerable<TelemetryConfig> telemetryConfig) {
        this.categoryName = categoryName;
        this.telemetryConfig = telemetryConfig;
    }

    private const string TagsActivityName = "OneFuzzLoggerActivity";

    public static Activity Activity {
        get {
            var cur = Activity.Current;
            if (cur is not null && string.Equals(cur.OperationName, TagsActivityName)) {
                return cur;
            } else {
                cur = new Activity(TagsActivityName);
            }
            return cur;
        }
    }

    /// <typeparam name="TState"></typeparam>
    /// <param name="state"></param>
    /// <returns></returns>
    IDisposable? ILogger.BeginScope<TState>(TState state) {
        var activity = new Activity(TagsActivityName);
        _ = activity.Start();
        return activity;
    }

    /// <param name="logLevel"></param>
    /// <returns></returns>
    bool ILogger.IsEnabled(LogLevel logLevel) {
        return logLevel != LogLevel.None && this.telemetryConfig.Any(c => c.TelemetryClient.IsEnabled());
    }

    /// <typeparam name="TState"></typeparam>
    /// <param name="logLevel"></param>
    /// <param name="eventId"></param>
    /// <param name="state"></param>
    /// <param name="exception"></param>
    /// <param name="formatter"></param>
    /// <exception cref="ArgumentNullException"></exception>
    void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        if (formatter == null) {
            throw new ArgumentNullException(nameof(formatter));
        }
        foreach (var config in this.telemetryConfig) {
            if (state is RequestTelemetry request && (config.EnabledTelemetry is null || config.EnabledTelemetry.Contains(Telemetry.Request))) {
                PopulateTags(request);
                config.TelemetryClient.TrackRequest(request);
            } else if (state is PageViewTelemetry pageView && (config.EnabledTelemetry is null || config.EnabledTelemetry.Contains(Telemetry.PageView))) {
                PopulateTags(pageView);
                config.TelemetryClient.TrackPageView(pageView);
            } else if (state is AvailabilityTelemetry availability && (config.EnabledTelemetry is null || config.EnabledTelemetry.Contains(Telemetry.Availability))) {
                PopulateTags(availability);
                config.TelemetryClient.TrackAvailability(availability);
            } else if (state is DependencyTelemetry dependency && (config.EnabledTelemetry is null || config.EnabledTelemetry.Contains(Telemetry.Dependency))) {
                PopulateTags(dependency);
                config.TelemetryClient.TrackDependency(dependency);
            } else if (state is MetricTelemetry metric && (config.EnabledTelemetry is null || config.EnabledTelemetry.Contains(Telemetry.Metric))) {
                PopulateTags(metric);
                config.TelemetryClient.TrackMetric(metric);
            } else if (state is EventTelemetry evt && (config.EnabledTelemetry is null || config.EnabledTelemetry.Contains(Telemetry.Event))) {
                PopulateTags(evt);
                config.TelemetryClient.TrackEvent(evt);
            } else {
                if ((this as ILogger).IsEnabled(logLevel)) {
                    if (exception is null) {
                        if (config.EnabledTelemetry is null || config.EnabledTelemetry.Contains(Telemetry.Trace)) {
                            TraceTelemetry traceTelemetry = new TraceTelemetry(
                                formatter(state, exception),
                                OneFuzzLogger.GetSeverityLevel(logLevel));
                            //https://github.com/microsoft/ApplicationInsights-dotnet/blob/248800626c1c31a2b4100f64a884257833b8c77f/BASE/src/Microsoft.ApplicationInsights/Extensibility/OperationCorrelationTelemetryInitializer.cs#L64
                            traceTelemetry.Context.Operation.Id = Activity.RootId;
                            traceTelemetry.Context.Operation.ParentId = Activity.SpanId.ToString();
                            this.PopulateTelemetry(traceTelemetry, state, eventId);
                            config.TelemetryClient.TrackTrace(traceTelemetry);
                        }
                    } else {
                        if (config.EnabledTelemetry is null || config.EnabledTelemetry.Contains(Telemetry.Exception)) {
                            ExceptionTelemetry exceptionTelemetry = new ExceptionTelemetry(exception) {
                                Message = exception.Message,
                                SeverityLevel = OneFuzzLogger.GetSeverityLevel(logLevel),
                            };
                            //https://github.com/microsoft/ApplicationInsights-dotnet/blob/248800626c1c31a2b4100f64a884257833b8c77f/BASE/src/Microsoft.ApplicationInsights/Extensibility/OperationCorrelationTelemetryInitializer.cs#L64
                            exceptionTelemetry.Context.Operation.Id = Activity.RootId;
                            exceptionTelemetry.Context.Operation.ParentId = Activity.SpanId.ToString();

                            exceptionTelemetry.Properties.Add("FormattedMessage", formatter(state, exception));
                            this.PopulateTelemetry(exceptionTelemetry, state, eventId);
                            config.TelemetryClient.TrackException(exceptionTelemetry);
                        }
                    }
                }
            }
        }
    }


    /// <summary>
    /// Converts the <see cref="LogLevel"/> into corresponding Application insights <see cref="SeverityLevel"/>.
    /// </summary>
    /// <param name="logLevel">Logging log level.</param>
    /// <returns>Application insights corresponding SeverityLevel for the LogLevel.</returns>
    private static SeverityLevel GetSeverityLevel(LogLevel logLevel) {
        switch (logLevel) {
            case LogLevel.Critical:
                return SeverityLevel.Critical;
            case LogLevel.Error:
                return SeverityLevel.Error;
            case LogLevel.Warning:
                return SeverityLevel.Warning;
            case LogLevel.Information:
                return SeverityLevel.Information;
            case LogLevel.Debug:
            case LogLevel.Trace:
            default:
                return SeverityLevel.Verbose;
        }
    }


    /// <summary>
    /// Populates the state, scope and event information for the logging event.
    /// </summary>
    /// <typeparam name="TState">State information for the current event.</typeparam>
    /// <param name="telemetryItem">Telemetry item.</param>
    /// <param name="state">Event state information.</param>
    /// <param name="eventId">Event Id information.</param>
    private void PopulateTelemetry<TState>(ISupportProperties telemetryItem, TState state, EventId eventId) {
        IDictionary<string, string> dict = telemetryItem.Properties;

        PopulateTags(telemetryItem);

        dict["CategoryName"] = this.categoryName;
        dict["Logger"] = nameof(OneFuzzLogger);

        if (eventId.Id != 0) {
            dict["EventId"] = eventId.Id.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrEmpty(eventId.Name)) {
            dict["EventName"] = eventId.Name;
        }
        if (state is IReadOnlyCollection<KeyValuePair<string, object>> stateDictionary) {
            foreach (KeyValuePair<string, object> item in stateDictionary) {
                if (string.Equals(item.Key, "{OriginalFormat}")) {
                    dict["OriginalFormat"] = Convert.ToString(item.Value, CultureInfo.InvariantCulture) ?? $"Failed to convert {item.Value}";
                } else {
                    //if there is an existing tag that is shadowing the log message tag - rename it
                    if (dict.ContainsKey(item.Key)) {
                        dict[$"!@#<--- {item.Key} --->#@!"] = dict[item.Key];
                    }
                    dict[item.Key] = Convert.ToString(item.Value, CultureInfo.InvariantCulture) ?? $"Failed to convert {item.Value}";
                }
            }
        }

    }

    /// <param name="telemetryItem"></param>
    private static void PopulateTags(ISupportProperties telemetryItem) {
        IDictionary<string, string> dict = telemetryItem.Properties;

        var ourActivities = new LinkedList<Activity>();
        {
            var activity = Activity;
            while (activity is not null && string.Equals(activity.OperationName, TagsActivityName)) {
                _ = ourActivities.AddFirst(activity);
                activity = activity.Parent;
            }
        }

        foreach (var activity in ourActivities) {
            foreach (var kv in activity.Tags) {
                dict[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? $"Failed to convert {kv.Value}";
            }
        }
    }
}


public static class OneFuzzLoggerExt {
    private static EventId EmptyEventId = new EventId(0);

    public static string? GetCorrelationId(this ILogger _) {
        foreach (var tag in OneFuzzLogger.Activity.Tags) {
            if (string.Equals(tag.Key, OneFuzzLogger.CorrelationId)) {
                return tag.Value;
            }
        }
        return null;
    }


    /// <param name="_"></param>
    /// <param name="key"></param>
    /// <param name="value"></param>
    public static void AddTag(this ILogger logger, string key, string value) {
        _ = OneFuzzLogger.Activity.AddTag(key, value);
    }

    /// <param name="_"></param>
    /// <param name="tags"></param>
    public static void AddTags(this ILogger logger, IDictionary<string, string> tags) {
        var activity = OneFuzzLogger.Activity;
        foreach (var kv in tags) {
            _ = activity.AddTag(kv.Key, kv.Value);
        }
    }

    /// <param name="_"></param>
    /// <param name="tags"></param>
    public static void AddTags(this ILogger logger, IEnumerable<(string, string)> tags) {
        var activity = OneFuzzLogger.Activity;
        foreach (var tag in tags) {
            _ = activity.AddTag(tag.Item1, tag.Item2);
        }
    }

    /// <param name="logger"></param>
    /// <param name="name"></param>
    /// <param name="metrics"></param>
    public static void LogEvent(this ILogger logger, string name, IDictionary<string, double>? metrics = null) {
        var evt = new EventTelemetry(name);
        if (metrics != null) {
            foreach (var m in metrics) {
                evt.Metrics[m.Key] = m.Value;
            }
        }
        logger.Log(LogLevel.Information, EmptyEventId, evt, null, (state, exception) => state.ToString() ?? $"Failed to convert event {name}");
    }

    /// <param name="logger"></param>
    /// <param name="name"></param>
    /// <param name="value"></param>
    public static void LogMetric(this ILogger logger, string name, double value) {
        var metric = new MetricTelemetry(name, value);
        logger.Log(LogLevel.Information, EmptyEventId, metric, null, (state, exception) => state.ToString() ?? $"Failed to convert metric {name}");
    }

    /// <param name="logger"></param>
    /// <param name="dependencyTypeName"></param>
    /// <param name="target"></param>
    /// <param name="dependencyName"></param>
    /// <param name="data"></param>
    /// <param name="startTime"></param>
    /// <param name="duration"></param>
    /// <param name="resultCode"></param>
    /// <param name="success"></param>
    public static void LogDependency(this ILogger logger, string dependencyTypeName, string target, string dependencyName, string data, DateTimeOffset startTime, TimeSpan duration, string resultCode, bool success) {
        var dependency = new DependencyTelemetry(dependencyTypeName, target, dependencyName, data, startTime, duration, resultCode, success);
        logger.Log(LogLevel.Information, EmptyEventId, dependency, null, (state, exception) => state.ToString() ?? $"Failed to convert dependency {dependencyName}");
    }

    /// <param name="logger"></param>
    /// <param name="name"></param>
    /// <param name="timeStamp"></param>
    /// <param name="duration"></param>
    /// <param name="runLocation"></param>
    /// <param name="success"></param>
    /// <param name="message"></param>
    public static void LogAvailabilityTelemetry(this ILogger logger, string name, DateTimeOffset timeStamp, TimeSpan duration, string runLocation, bool success, string? message = null) {
        var availability = new AvailabilityTelemetry(name, timeStamp, duration, runLocation, success, message);
        logger.Log(LogLevel.Information, EmptyEventId, availability, null, (state, exception) => state.ToString() ?? $"Failed to convert availability {availability}");
    }

    /// <param name="logger"></param>
    /// <param name="pageName"></param>
    public static void LogPageView(this ILogger logger, string pageName) {
        var pageView = new PageViewTelemetry(pageName);
        logger.Log(LogLevel.Information, EmptyEventId, pageView, null, (state, exception) => state.ToString() ?? $"Failed to convert pageView {pageView}");
    }

    /// <param name="logger"></param>
    /// <param name="name"></param>
    /// <param name="startTime"></param>
    /// <param name="duration"></param>
    /// <param name="responseCode"></param>
    /// <param name="success"></param>
    public static void LogRequest(this ILogger logger, string name, DateTimeOffset startTime, TimeSpan duration, string responseCode, bool success) {
        var request = new RequestTelemetry(name, startTime, duration, responseCode, success);
        logger.Log(LogLevel.Information, EmptyEventId, request, null, (state, exception) => state.ToString() ?? $"Failed to convert request {request}");
    }
}


[ProviderAlias("OneFuzzLoggerProvider")]
public sealed class OneFuzzLoggerProvider : ILoggerProvider {
    private readonly ConcurrentDictionary<string, OneFuzzLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly IEnumerable<TelemetryConfig> telemetryConfigs;

    /// <param name="telemetryConfigs"></param>
    public OneFuzzLoggerProvider(IEnumerable<TelemetryConfig> telemetryConfigs) {
        this.telemetryConfigs = telemetryConfigs;
    }
    /// <param name="categoryName"></param>
    /// <returns></returns>
    public ILogger CreateLogger(string categoryName) {
        return _loggers.GetOrAdd(categoryName, name => new OneFuzzLogger(name, telemetryConfigs));
    }

    public void Dispose() {
        _loggers.Clear();
    }
}
