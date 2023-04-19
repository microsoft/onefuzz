using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

/// <summary>
/// Identifies the enum type associated with the metric class
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class MetricTypeAttribute : Attribute {
    public MetricTypeAttribute(MetricType metricType) {
        this.MetricType = metricType;
    }

    public MetricType MetricType { get; }
}


public enum MetricType {
    JobCreated,
    JobStopped,
    NodeCreated,
    NodeDeleted,
    NodeStateUpdated,
    Ping,
    PoolCreated,
    PoolDeleted,
    ProxyCreated,
    ProxyDeleted,
    ProxyFailed,
    ProxyStateUpdated,
    ScalesetCreated,
    ScalesetDeleted,
    ScalesetFailed,
    ScalesetStateUpdated,
    ScalesetResizeScheduled,
    TaskCreated,
    TaskFailed,
    TaskStateUpdated,
    TaskStopped,
    CrashReported,
    RegressionReported,
    FileAdded,
    TaskHeartbeat,
    NodeHeartbeat,
    InstanceConfigUpdated,
    NotificationFailed
}

public abstract record BaseMetric() {

    private static readonly IReadOnlyDictionary<Type, MetricType> typeToMetric;
    private static readonly IReadOnlyDictionary<MetricType, Type> metricToType;
    private static int metricValue;

    static BaseMetric() {

        MetricType ExtractMetricType(Type type) {
            var attr = type.GetCustomAttribute<MetricTypeAttribute>();
            if (attr is null) {
                throw new InvalidOperationException($"Type {type} is missing {nameof(MetricTypeAttribute)}");
            }
            return attr.MetricType;
        }

        typeToMetric =
            typeof(BaseMetric).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(BaseMetric)))
            .ToDictionary(x => x, ExtractMetricType);

        metricToType = typeToMetric.ToDictionary(x => x.Value, x => x.Key);

        // check that all Metric types are accounted for
        var allMetricTypes = Enum.GetValues<MetricType>();
        var missingMetricTypes = allMetricTypes.Except(metricToType.Keys).ToList();
        if (missingMetricTypes.Any()) {
            throw new InvalidOperationException($"Missing metric types: {string.Join(", ", missingMetricTypes)}");
        }

        metricValue = 1;
    }


    public MetricType GetMetricType() {
        var type = this.GetType();
        if (typeToMetric.TryGetValue(type, out var metricType)) {
            return metricType;
        }

        throw new NotSupportedException($"Unknown metric type: {type.GetType()}");
    }

    public static Type GetTypeInfo(MetricType metricType) {
        if (metricToType.TryGetValue(metricType, out var type)) {
            return type;
        }

        throw new ArgumentException($"Unknown metric type: {metricType}");
    }

    public static void SetMetricValue(int value) {
        metricValue = value;
    }

    public static int GetMetricValue() {
        return metricValue;
    }
};

public class MetricTypeProvider : ITypeProvider {
    public Type GetTypeInfo(object input) {
        return BaseMetric.GetTypeInfo((input as MetricType?) ?? throw new ArgumentException($"input is expected to be an MetricType {input}"));
    }
}

[MetricType(MetricType.TaskStopped)]
public record MetricTaskStopped(
    Guid JobId,
    Guid TaskId,
    UserInfo? UserInfo,
    TaskConfig Config
) : BaseMetric();

[MetricType(MetricType.TaskFailed)]
public record MetricTaskFailed(
    Guid JobId,
    Guid TaskId,
    Error Error,
    UserInfo? UserInfo,
    TaskConfig Config
    ) : BaseMetric();


[MetricType(MetricType.JobCreated)]
public record MetricJobCreated(
   Guid JobId,
   JobConfig Config,
   UserInfo? UserInfo
   ) : BaseMetric();

[MetricType(MetricType.JobStopped)]
public record MetricJobStopped(
    Guid JobId,
    JobConfig Config,
    UserInfo? UserInfo,
    List<JobTaskStopped> TaskInfo
) : BaseMetric(), ITruncatable<BaseMetric> {
    public BaseMetric Truncate(int maxLength) {
        return this with {
            Config = Config.Truncate(maxLength)
        };
    }
}

[MetricType(MetricType.TaskCreated)]
public record MetricTaskCreated(
    Guid JobId,
    Guid TaskId,
    TaskConfig Config,
    UserInfo? UserInfo
    ) : BaseMetric();

[MetricType(MetricType.TaskStateUpdated)]
public record MetricTaskStateUpdated(
    Guid JobId,
    Guid TaskId,
    TaskState State,
    DateTimeOffset? EndTime,
    TaskConfig Config
    ) : BaseMetric();


[MetricType(MetricType.TaskHeartbeat)]
public record MetricTaskHeartbeat(
   Guid JobId,
   Guid TaskId,
   TaskConfig Config
) : BaseMetric();

[MetricType(MetricType.Ping)]
public record MetricPing(
    Guid PingId
) : BaseMetric();


[MetricType(MetricType.ScalesetCreated)]
public record MetricScalesetCreated(
   Guid ScalesetId,
   PoolName PoolName,
   string VmSku,
   string Image,
   Region Region,
   int Size) : BaseMetric();


[MetricType(MetricType.ScalesetFailed)]
public sealed record MetricScalesetFailed(
    Guid ScalesetId,
    PoolName PoolName,
    Error Error
) : BaseMetric();


[MetricType(MetricType.ScalesetDeleted)]
public record MetricScalesetDeleted(
   Guid ScalesetId,
   PoolName PoolName

   ) : BaseMetric();


[MetricType(MetricType.ScalesetResizeScheduled)]
public record MetricScalesetResizeScheduled(
    Guid ScalesetId,
    PoolName PoolName,
    long size
    ) : BaseMetric();


[MetricType(MetricType.PoolDeleted)]
public record MetricPoolDeleted(
   PoolName PoolName
   ) : BaseMetric();


[MetricType(MetricType.PoolCreated)]
public record MetricPoolCreated(
   PoolName PoolName,
   Os Os,
   Architecture Arch,
   bool Managed
   // ignoring AutoScaleConfig because it's not used anymore
   //AutoScaleConfig? Autoscale
   ) : BaseMetric();


[MetricType(MetricType.ProxyCreated)]
public record MetricProxyCreated(
   Region Region,
   Guid? ProxyId
   ) : BaseMetric();


[MetricType(MetricType.ProxyDeleted)]
public record MetricProxyDeleted(
   Region Region,
   Guid? ProxyId
) : BaseMetric();


[MetricType(MetricType.ProxyFailed)]
public record MetricProxyFailed(
   Region Region,
   Guid? ProxyId,
   Error Error
) : BaseMetric();


[MetricType(MetricType.ProxyStateUpdated)]
public record MetricProxyStateUpdated(
   Region Region,
   Guid ProxyId,
   VmState State
   ) : BaseMetric();


[MetricType(MetricType.NodeCreated)]
public record MetricNodeCreated(
    Guid MachineId,
    Guid? ScalesetId,
    PoolName PoolName
    ) : BaseMetric();

[MetricType(MetricType.NodeHeartbeat)]
public record MetricNodeHeartbeat(
    Guid MachineId,
    Guid? ScalesetId,
    PoolName PoolName,
    int Value
    ) : BaseMetric();


[MetricType(MetricType.NodeDeleted)]
public record MetricNodeDeleted(
    Guid MachineId,
    Guid? ScalesetId,
    PoolName PoolName,
    NodeState? MachineState
) : BaseMetric();


[MetricType(MetricType.ScalesetStateUpdated)]
public record MetricScalesetStateUpdated(
    Guid ScalesetId,
    PoolName PoolName,
    ScalesetState State
) : BaseMetric();

[MetricType(MetricType.NodeStateUpdated)]
public record MetricNodeStateUpdated(
    Guid MachineId,
    Guid? ScalesetId,
    PoolName PoolName,
    NodeState state
    ) : BaseMetric();

[MetricType(MetricType.CrashReported)]
public record MetricCrashReported(
    Report Report,
    Container Container,
    [property: JsonPropertyName("filename")] String FileName,
    TaskConfig? TaskConfig
) : BaseMetric(), ITruncatable<BaseMetric> {
    public BaseMetric Truncate(int maxLength) {
        return this with {
            Report = Report.Truncate(maxLength)
        };
    }
}

[MetricType(MetricType.RegressionReported)]
public record MetricRegressionReported(
    RegressionReport RegressionReport,
    Container Container,
    [property: JsonPropertyName("filename")] String FileName,
    TaskConfig? TaskConfig
) : BaseMetric(), ITruncatable<BaseMetric> {
    public BaseMetric Truncate(int maxLength) {
        return this with {
            RegressionReport = RegressionReport.Truncate(maxLength)
        };
    }
}

[MetricType(MetricType.FileAdded)]
public record MetricFileAdded(
    Container Container,
    [property: JsonPropertyName("filename")] String FileName
) : BaseMetric();


[MetricType(MetricType.InstanceConfigUpdated)]
public record MetricInstanceConfigUpdated(
    InstanceConfig Config
) : BaseMetric();

[MetricType(MetricType.NotificationFailed)]
public record MetricNotificationFailed(
    Guid NotificationId,
    Guid JobId,
    Error? Error
) : BaseMetric();

public record MetricMessage(
    Guid MetricId,
    MetricType MetricType,
    [property: TypeDiscrimnatorAttribute("MetricType", typeof(MetricTypeProvider))]
    [property: JsonConverter(typeof(BaseMetricConverter))]
    BaseMetric Metric,
    Guid InstanceId,
    String InstanceName
);

public class BaseMetricConverter : JsonConverter<BaseMetric> {
    public override BaseMetric? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        return null;
    }

    public override void Write(Utf8JsonWriter writer, BaseMetric value, JsonSerializerOptions options) {
        var metricType = value.GetType();
        JsonSerializer.Serialize(writer, value, metricType, options);
    }
}
