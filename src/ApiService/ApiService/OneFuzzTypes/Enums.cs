using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

[SerializeValue]
public enum ErrorCode {
    INVALID_REQUEST = 450,
    INVALID_PERMISSION = 451,
    MISSING_EULA_AGREEMENT = 452,
    INVALID_JOB = 453,
    INVALID_TASK = 493,
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
    UNABLE_TO_CREATE_CONTAINER = 474,
    UNABLE_TO_DOWNLOAD_FILE = 475,
    VM_UPDATE_FAILED = 476,
    UNSUPPORTED_FIELD_OPERATION = 477,
    ADO_VALIDATION_INVALID_PAT = 478,
    ADO_VALIDATION_INVALID_FIELDS = 479,
    GITHUB_VALIDATION_INVALID_PAT = 480,
    GITHUB_VALIDATION_INVALID_REPOSITORY = 481,
    UNEXPECTED_DATA_SHAPE = 482,
    UNABLE_TO_SEND = 483,
    NODE_DELETED = 484,
    TASK_CANCELLED = 485,
    SCALE_IN_PROTECTION_UPDATE_ALREADY_IN_PROGRESS = 486,
    SCALE_IN_PROTECTION_INSTANCE_NO_LONGER_EXISTS = 487,
    SCALE_IN_PROTECTION_REACHED_MODEL_LIMIT = 488,
    SCALE_IN_PROTECTION_UNEXPECTED_ERROR = 489,
    ADO_VALIDATION_UNEXPECTED_HTTP_EXCEPTION = 490,
    ADO_VALIDATION_UNEXPECTED_ERROR = 491,
    ADO_VALIDATION_MISSING_PAT_SCOPES = 492,
    ADO_WORKITEM_PROCESSING_DISABLED = 494,
    // NB: if you update this enum, also update enums.py
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
    DotnetCoverage,
    DotnetCrashReport,
    LibfuzzerDotnetFuzz,
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
    Logs,
    ExtraSetup,
    ExtraOutput,
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
    private static readonly HashSet<ScalesetState> _canUpdate =
        new() {
            ScalesetState.Init,
            ScalesetState.Resize,
        };

    private static readonly HashSet<ScalesetState> _needsWork =
        new() {
            ScalesetState.Init,
            ScalesetState.Setup,
            ScalesetState.Resize,
            ScalesetState.Shutdown,
            ScalesetState.Halt,
        };

    private static readonly HashSet<ScalesetState> _available =
        new() {
            ScalesetState.Resize,
            ScalesetState.Running,
        };

    private static readonly HashSet<ScalesetState> _resizing =
        new() {
            ScalesetState.Halt,
            ScalesetState.Init,
            ScalesetState.Setup,
        };

    /// set of states that indicate the scaleset can be updated
    public static bool CanUpdate(this ScalesetState state) => _canUpdate.Contains(state);
    public static IReadOnlySet<ScalesetState> CanUpdateStates => _canUpdate;

    /// set of states that indicate work is needed during eventing
    public static bool NeedsWork(this ScalesetState state) => _needsWork.Contains(state);
    public static IReadOnlySet<ScalesetState> NeedsWorkStates => _needsWork;

    /// set of states that indicate if it's available for work
    public static bool IsAvailable(this ScalesetState state) => _available.Contains(state);
    public static IReadOnlySet<ScalesetState> AvailableStates => _available;

    /// set of states that indicate scaleset is resizing
    public static bool IsResizing(this ScalesetState state) => _resizing.Contains(state);
    public static IReadOnlySet<ScalesetState> ResizingStates => _resizing;
}


public static class VmStateHelper {

    private static readonly IReadOnlySet<VmState> _needsWork = new HashSet<VmState> { VmState.Init, VmState.ExtensionsLaunch, VmState.Stopping };
    private static readonly IReadOnlySet<VmState> _available = new HashSet<VmState> { VmState.Init, VmState.ExtensionsLaunch, VmState.ExtensionsFailed, VmState.VmAllocationFailed, VmState.Running, };

    public static IReadOnlySet<VmState> NeedsWork => _needsWork;
    public static IReadOnlySet<VmState> Available => _available;
}

public static class TaskStateHelper {

    public static readonly IReadOnlySet<TaskState> AvailableStates =
        new HashSet<TaskState> { TaskState.Waiting, TaskState.Scheduled, TaskState.SettingUp, TaskState.Running, TaskState.WaitJob };

    public static readonly IReadOnlySet<TaskState> NeedsWorkStates =
        new HashSet<TaskState> { TaskState.Init, TaskState.Stopping };

    public static readonly IReadOnlySet<TaskState> ShuttingDownStates =
        new HashSet<TaskState> { TaskState.Stopping, TaskState.Stopped };

    public static readonly IReadOnlySet<TaskState> HasStartedStates =
        new HashSet<TaskState> { TaskState.Running, TaskState.Stopping, TaskState.Stopped };

    public static bool Available(this TaskState state) => AvailableStates.Contains(state);

    public static bool NeedsWork(this TaskState state) => NeedsWorkStates.Contains(state);

    public static bool ShuttingDown(this TaskState state) => ShuttingDownStates.Contains(state);

    public static bool HasStarted(this TaskState state) => HasStartedStates.Contains(state);

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
    ModuleAllowlist,
    SourceAllowlist,
    TargetMustUseInput,
    TargetAssembly,
    TargetClass,
    TargetMethod,
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
    private static readonly IReadOnlySet<NodeState> _needsWork =
        new HashSet<NodeState>(new[] { NodeState.Done, NodeState.Shutdown, NodeState.Halt });

    private static readonly IReadOnlySet<NodeState> _readyForReset
        = new HashSet<NodeState>(new[] { NodeState.Done, NodeState.Shutdown, NodeState.Halt });

    private static readonly IReadOnlySet<NodeState> _canProcessNewWork =
        new HashSet<NodeState>(new[] { NodeState.Free });

    private static readonly IReadOnlySet<NodeState> _busy =
        new HashSet<NodeState>(new[] { NodeState.Busy });

    public static IReadOnlySet<NodeState> BusyStates => _busy;

    public static IReadOnlySet<NodeState> NeedsWorkStates => _needsWork;

    public static bool NeedsWork(this NodeState state) => _needsWork.Contains(state);

    ///If Node is in one of these states, ignore updates from the agent.
    public static bool ReadyForReset(this NodeState state) => _readyForReset.Contains(state);

    public static bool CanProcessNewWork(this NodeState state) => _canProcessNewWork.Contains(state);
}


/// Select how nodes should be disposed of after they complete a WorkSet
public enum NodeDisposalStrategy {
    /// Re-images the node (which resets its state), then either it can pick up more work
    /// or auto scale will reap it if no work is queued
    ScaleIn,

    /// Skips re-imaging the node, the node will no longer pick up new work. It will only be
    /// scaled in by auto scale.
    Decommission
}


public enum GithubIssueState {
    Open,
    Closed
}

public enum GithubIssueSearchMatch {
    Title,
    Body
}
