using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using global::Microsoft.ApplicationInsights.DataContracts;
using global::Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace IntegrationTests;

/// <summary>
/// 
/// </summary>
public enum Telemetry {
    /// <summary>
    /// 
    /// </summary>
    Trace,
    /// <summary>
    /// 
    /// </summary>
    Exception,
    /// <summary>
    /// 
    /// </summary>
    Request,
    /// <summary>
    /// 
    /// </summary>
    Dependency,
    /// <summary>
    /// 
    /// </summary>
    PageView,
    /// <summary>
    /// 
    /// </summary>
    Availability,
    /// <summary>
    /// 
    /// </summary>
    Metric,
    /// <summary>
    /// 
    /// </summary>
    Event
}


/// <summary>
/// 
/// </summary>
public class OneFuzzLogger : ILogger {

    private readonly ITestOutputHelper _output;

    /// <summary>
    /// 
    /// </summary>
    public const string CorrelationId = "CorrelationId";
    private readonly string categoryName;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="categoryName"></param>
    /// <param name="telemetryConfig"></param>
    public OneFuzzLogger(string categoryName, ITestOutputHelper output) {
        this.categoryName = categoryName;
        this._output = output;

    }

    private const string TagsActivityName = "OneFuzzLoggerActivity";

    /// <summary>
    /// 
    /// </summary>
    public static Activity Activity {
        get {
            var cur = Activity.Current;
            while (cur is not null && string.Equals(cur.OperationName, TagsActivityName)) {
                cur = cur.Parent;
            }

            if (cur is null) {
                cur = new Activity(TagsActivityName);
                _ = cur.Start();
            }
            return cur;
        }
    }

    /// <summary>
    /// /
    /// </summary>
    /// <typeparam name="TState"></typeparam>
    /// <param name="state"></param>
    /// <returns></returns>
    IDisposable? ILogger.BeginScope<TState>(TState state) {
        var activity = new Activity(TagsActivityName);
        _ = activity.Start();
        return activity;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="logLevel"></param>
    /// <returns></returns>
    bool ILogger.IsEnabled(LogLevel logLevel) {
        return true;
    }

    /// <summary>
    /// 
    /// </summary>
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

        if (state is RequestTelemetry request) {
            PopulateTags(request);
            _output.WriteLine($"[Request] {request}");
        } else if (state is PageViewTelemetry pageView) {
            PopulateTags(pageView);
            _output.WriteLine($"[PageView] {pageView}");
        } else if (state is AvailabilityTelemetry availability) {
            PopulateTags(availability);
            _output.WriteLine($"[Availability] {availability}");
        } else if (state is DependencyTelemetry dependency) {
            PopulateTags(dependency);
            _output.WriteLine($"[Dependency] {dependency}");
        } else if (state is MetricTelemetry metric) {
            PopulateTags(metric);
            _output.WriteLine($"[Metric] {metric}");
        } else if (state is EventTelemetry evt) {
            PopulateTags(evt);
            _output.WriteLine($"[Event] {evt}");
        } else {
            if ((this as ILogger).IsEnabled(logLevel)) {
                if (exception is null) {
                    TraceTelemetry traceTelemetry = new TraceTelemetry(
                        formatter(state, exception),
                        OneFuzzLogger.GetSeverityLevel(logLevel));


                    traceTelemetry.Context.Operation.Id = Activity.RootId;
                    traceTelemetry.Context.Operation.ParentId = Activity.SpanId.ToString();
                    this.PopulateTelemetry(traceTelemetry, state, eventId);
                    _output.WriteLine($"[Trace] {traceTelemetry}");

                } else {
                    ExceptionTelemetry exceptionTelemetry = new ExceptionTelemetry(exception) {
                        Message = exception.Message,
                        SeverityLevel = OneFuzzLogger.GetSeverityLevel(logLevel),
                    };
                    exceptionTelemetry.Context.Operation.Id = Activity.RootId;
                    exceptionTelemetry.Context.Operation.ParentId = Activity.SpanId.ToString();

                    exceptionTelemetry.Properties.Add("FormattedMessage", formatter(state, exception));
                    this.PopulateTelemetry(exceptionTelemetry, state, eventId);
                    _output.WriteLine($"[Exception] {exceptionTelemetry}");
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
                    dict["OriginalFormat"] = Convert.ToString(item.Value, CultureInfo.InvariantCulture) ?? $"Faled to convert {item.Value}";
                } else {
                    dict[item.Key] = Convert.ToString(item.Value, CultureInfo.InvariantCulture) ?? $"Faled to convert {item.Value}";
                }
            }
        }
        PopulateTags(telemetryItem);
    }

    /// <summary>
    /// 
    /// </summary>
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
                dict[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? $"Faled to convert {kv.Value}";
            }
        }
    }
}


/// <summary>
/// 
/// </summary>
public static class OneFuzzLoggerExt {
    private static EventId EmptyEventId = new EventId(0);

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public static string? GetCorrelationId(this ILogger _) {
        foreach (var tag in OneFuzzLogger.Activity.Tags) {
            if (string.Equals(tag.Key, OneFuzzLogger.CorrelationId)) {
                return tag.Value;
            }
        }
        return null;
    }


    /// <summary>
    /// 
    /// </summary>
    /// <param name="_"></param>
    /// <param name="key"></param>
    /// <param name="value"></param>
    public static void AddTag(this ILogger logger, string key, string value) {
        _ = OneFuzzLogger.Activity.AddTag(key, value);
    }

    /// <summary>
    /// 
    /// </summary>
    /// /// <param name="_"></param>
    /// <param name="tags"></param>
    public static void AddTags(this ILogger logger, IDictionary<string, string> tags) {
        var activity = OneFuzzLogger.Activity;
        foreach (var kv in tags) {
            _ = activity.AddTag(kv.Key, kv.Value);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="_"></param>
    /// <param name="tags"></param>
    public static void AddTags(this ILogger logger, IEnumerable<(string, string)> tags) {
        var activity = OneFuzzLogger.Activity;
        foreach (var tag in tags) {
            _ = activity.AddTag(tag.Item1, tag.Item2);
        }
    }

    /// <summary>
    /// 
    /// </summary>
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


    /// <summary>
    /// 
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="name"></param>
    /// <param name="value"></param>
    public static void LogMetric(this ILogger logger, string name, double value) {
        var metric = new MetricTelemetry(name, value);
        logger.Log(LogLevel.Information, EmptyEventId, metric, null, (state, exception) => state.ToString() ?? $"Failed to convert metric {name}");
    }

    /// <summary>
    /// 
    /// </summary>
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

    /// <summary>
    /// 
    /// </summary>
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

    /// <summary>
    ///
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="pageName"></param>
    public static void LogPageView(this ILogger logger, string pageName) {
        var pageView = new PageViewTelemetry(pageName);
        logger.Log(LogLevel.Information, EmptyEventId, pageView, null, (state, exception) => state.ToString() ?? $"Failed to convert pageView {pageView}");
    }

    /// <summary>
    ///
    /// </summary>
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



/// <summary>
/// 
/// </summary>
[ProviderAlias("OneFuzzLoggerProvider")]
public sealed class OneFuzzLoggerProvider : ILoggerProvider, ILoggerFactory {
    private readonly ConcurrentDictionary<string, OneFuzzLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ITestOutputHelper _output;



    /// <summary>
    /// 
    /// </summary>
    /// <param name="telemetryConfigs"></param>
    public OneFuzzLoggerProvider(ITestOutputHelper output) {
        _output = output;
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="categoryName"></param>
    /// <returns></returns>
    public ILogger CreateLogger(string categoryName) {
        return _loggers.GetOrAdd(categoryName, name => new OneFuzzLogger(name, _output));
    }

    public ILogger<T> CreateLogger<T>() {
        return new Logger<T>(this);
    }


    /// <summary>
    /// 
    /// </summary>
    public void Dispose() {
        _loggers.Clear();
    }

    public void AddProvider(ILoggerProvider provider) {

    }
}
