using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

[SerializeValue]
public enum ErrorCode {
    INVALID_REQUEST = 450,
    INVALID_PERMISSION = 451,
    MISSING_EULA_AGREEMENT = 452,
    INVALID_JOB = 453,
    INVALID_TASK = 453,
    UNABLE_TO_ADD_TASK_TO_JOB = 454,
    INVALID_CONTAINER = 455,
    UNABLE_TO_RESIZE = 456,
    UNAUTHORIZED = 457,
    UNABLE_TO_USE_STOPPED_JOB = 458,
    UNABLE_TO_CHANGE_JOB_DURATION = 459,
    UNABLE_TO_CREATE_NETWORK = 460,
    VM_CREATE_FAILED = 461,
    MISSING_NOTIFICATION = 462,
    INVALID_IMAGE = 463,
    UNABLE_TO_CREATE = 464,
    UNABLE_TO_PORT_FORWARD = 465,
    UNABLE_TO_FIND = 467,
    TASK_FAILED = 468,
    INVALID_NODE = 469,
    NOTIFICATION_FAILURE = 470,
    UNABLE_TO_UPDATE = 471,
    PROXY_FAILED = 472,
    INVALID_CONFIGURATION = 473,
}

public enum VmState {
    Init,
    ExtensionsLaunch,
    ExtensionsFailed,
    VmAllocationFailed,
    Running,
    Stopping,
    Stopped
}

public enum WebhookMessageState {
    Queued,
    Retrying,
    Succeeded,
    Failed
}

public enum TaskState {
    Init,
    Waiting,
    Scheduled,
    SettingUp,
    Running,
    Stopping,
    Stopped,
    WaitJob
}

public enum TaskType {
    Coverage,
    LibfuzzerFuzz,
    LibfuzzerCoverage,
    LibfuzzerCrashReport,
    LibfuzzerMerge,
    LibfuzzerRegression,
    GenericAnalysis,
    GenericSupervisor,
    GenericMerge,
    GenericGenerator,
    GenericCrashReport,
    GenericRegression,
    DotnetCoverage,
}

public enum Os {
    Windows,
    Linux
}

public enum ContainerType {
    Analysis,
    Coverage,
    Crashes,
    Inputs,
    NoRepro,
    ReadonlyInputs,
    Reports,
    Setup,
    Tools,
    UniqueInputs,
    UniqueReports,
    RegressionReports,
    Logs
}


[SkipRename]
public enum StatsFormat {
    AFL
}

public enum TaskDebugFlag {
    KeepNodeOnFailure,
    KeepNodeOnCompletion,
}

public enum ScalesetState {
    Init,
    Setup,
    Resize,
    Running,
    Shutdown,
    Halt,
    CreationFailed
}


public enum JobState {
    Init,
    Enabled,
    Stopping,
    Stopped
}

public static class JobStateHelper {
    private static readonly IReadOnlySet<JobState> _shuttingDown = new HashSet<JobState>(new[] { JobState.Stopping, JobState.Stopped });
    private static readonly IReadOnlySet<JobState> _avaiable = new HashSet<JobState>(new[] { JobState.Init, JobState.Enabled });
    private static readonly IReadOnlySet<JobState> _needsWork = new HashSet<JobState>(new[] { JobState.Init, JobState.Stopping });

    public static IReadOnlySet<JobState> Available => _avaiable;
    public static IReadOnlySet<JobState> NeedsWork => _needsWork;
    public static IReadOnlySet<JobState> ShuttingDown => _shuttingDown;
}



public static class ScalesetStateHelper {
    private static readonly IReadOnlySet<ScalesetState> _canUpdate = new HashSet<ScalesetState> { ScalesetState.Init, ScalesetState.Resize };
    private static readonly IReadOnlySet<ScalesetState> _needsWork =
        new HashSet<ScalesetState>{
            ScalesetState.Init,
            ScalesetState.Setup,
            ScalesetState.Resize,
            ScalesetState.Shutdown,
            ScalesetState.Halt
        };
    private static readonly IReadOnlySet<ScalesetState> _available = new HashSet<ScalesetState> { ScalesetState.Resize, ScalesetState.Running };
    private static readonly IReadOnlySet<ScalesetState> _resizing = new HashSet<ScalesetState> { ScalesetState.Halt, ScalesetState.Init, ScalesetState.Setup };

    /// set of states that indicate the scaleset can be updated
    public static IReadOnlySet<ScalesetState> CanUpdate => _canUpdate;

    /// set of states that indicate work is needed during eventing
    public static IReadOnlySet<ScalesetState> NeedsWork => _needsWork;

    /// set of states that indicate if it's available for work
    public static IReadOnlySet<ScalesetState> Available => _available;

    /// set of states that indicate scaleset is resizing
    public static IReadOnlySet<ScalesetState> Resizing => _resizing;
}


public static class VmStateHelper {

    private static readonly IReadOnlySet<VmState> _needsWork = new HashSet<VmState> { VmState.Init, VmState.Init, VmState.ExtensionsLaunch, VmState.Stopping };
    private static readonly IReadOnlySet<VmState> _available = new HashSet<VmState> { VmState.Init, VmState.ExtensionsLaunch, VmState.ExtensionsFailed, VmState.VmAllocationFailed, VmState.Running, };

    public static IReadOnlySet<VmState> NeedsWork => _needsWork;
    public static IReadOnlySet<VmState> Available => _available;
}

public static class TaskStateHelper {

    private static readonly IReadOnlySet<TaskState> _available = new HashSet<TaskState> { TaskState.Waiting, TaskState.Scheduled, TaskState.SettingUp, TaskState.Running, TaskState.WaitJob };
    private static readonly IReadOnlySet<TaskState> _needsWork = new HashSet<TaskState> { TaskState.Init, TaskState.Stopping };
    private static readonly IReadOnlySet<TaskState> _shuttingDown = new HashSet<TaskState> { TaskState.Stopping, TaskState.Stopped };
    private static readonly IReadOnlySet<TaskState> _hasStarted = new HashSet<TaskState> { TaskState.Running, TaskState.Stopping, TaskState.Stopped };

    public static IReadOnlySet<TaskState> Available => _available;

    public static IReadOnlySet<TaskState> NeedsWork => _needsWork;

    public static IReadOnlySet<TaskState> ShuttingDown => _shuttingDown;

    public static IReadOnlySet<TaskState> HasStarted => _hasStarted;

}
public enum PoolState {
    Init,
    Running,
    Shutdown,
    Halt
}

public static class PoolStateHelper {
    private static readonly IReadOnlySet<PoolState> _needsWork = new HashSet<PoolState> { PoolState.Init, PoolState.Shutdown, PoolState.Halt };
    private static readonly IReadOnlySet<PoolState> _available = new HashSet<PoolState> { PoolState.Running };

    public static IReadOnlySet<PoolState> NeedsWork => _needsWork;
    public static IReadOnlySet<PoolState> Available => _available;
}

[SkipRename]
public enum Architecture {
    x86_64
}

public enum TaskFeature {
    InputQueueFromContainer,
    SupervisorExe,
    SupervisorEnv,
    SupervisorOptions,
    SupervisorInputMarker,
    StatsFile,
    StatsFormat,
    TargetExe,
    TargetExeOptional,
    TargetEnv,
    TargetOptions,
    AnalyzerExe,
    AnalyzerEnv,
    AnalyzerOptions,
    RenameOutput,
    TargetOptionsMerge,
    TargetWorkers,
    GeneratorExe,
    GeneratorEnv,
    GeneratorOptions,
    WaitForFiles,
    TargetTimeout,
    CheckAsanLog,
    CheckDebugger,
    CheckRetryCount,
    EnsembleSyncDelay,
    PreserveExistingOutputs,
    CheckFuzzerHelp,
    ExpectCrashOnFailure,
    ReportList,
    MinimizedStackDepth,
    CoverageFilter,
    TargetMustUseInput
}


[Flags]
public enum ContainerPermission {
    Read = 1 << 0,
    Write = 1 << 1,
    List = 1 << 2,
    Delete = 1 << 3,
}


public enum Compare {
    Equal,
    AtLeast,
    AtMost
}
public enum AgentMode {
    Fuzz,
    Repro,
    Proxy
}

public enum NodeState {
    Init,
    Free,
    SettingUp,
    Rebooting,
    Ready,
    Busy,
    Done,
    Shutdown,
    Halt,
}

public static class NodeStateHelper {

    private static readonly IReadOnlySet<NodeState> _needsWork = new HashSet<NodeState>(new[] { NodeState.Done, NodeState.Shutdown, NodeState.Halt });
    private static readonly IReadOnlySet<NodeState> _readyForReset = new HashSet<NodeState>(new[] { NodeState.Done, NodeState.Shutdown, NodeState.Halt });
    private static readonly IReadOnlySet<NodeState> _canProcessNewWork = new HashSet<NodeState>(new[] { NodeState.Free });


    public static IReadOnlySet<NodeState> NeedsWork => _needsWork;

    ///If Node is in one of these states, ignore updates from the agent.
    public static IReadOnlySet<NodeState> ReadyForReset => _readyForReset;

    public static IReadOnlySet<NodeState> CanProcessNewWork => _canProcessNewWork;
}


public enum NodeDisposalStrategy {
    ScaleIn,
    Decomission
}
