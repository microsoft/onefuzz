using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace FunctionalTests;


public class TaskDetails {
    readonly JsonElement _e;
    public TaskDetails(JsonElement e) => _e = e;

    public string Type => _e.GetStringProperty("type");

    public long Duration => _e.GetLongProperty("duration");
    public string? TargetExe => _e.GetStringProperty("target_exe");

    public IDictionary<string, string>? TargetEnv => _e.GetNullableStringDictProperty("target_env");

    public IEnumerable<string>? TargetOptions => _e.GetEnumerableStringProperty("target_options");

    public long? TargetWorkers => _e.GetNullableLongProperty("target_workers");

    public bool? TargetOptionsMerge => _e.GetNullableBoolProperty("target_option_merge");

    public bool? CheckAsanLog => _e.GetNullableBoolProperty("check_asan_log");

    public bool? CheckDebugger => _e.GetNullableBoolProperty("check_debugger");

    public long? CheckRetryCount => _e.GetNullableLongProperty("check_retry_count");

    public bool? CheckFuzzerHelp => _e.GetNullableBoolProperty("check_fuzzer_help");

    public bool? ExpectCrashOnFailure => _e.GetNullableBoolProperty("expect_crash_on_failure");

    public bool? RenameOutput => _e.GetNullableBoolProperty("rename_output");

    public string? SupervisorExe => _e.GetNullableStringProperty("supervisor_exe");

    public IDictionary<string, string>? SupervisonEnv => _e.GetNullableStringDictProperty("supervisor_env");

    public IEnumerable<string>? SupervisorOptions => _e.GetEnumerableNullableStringProperty("supervisor_options");

    public string? SupervisorInputMarker => _e.GetStringProperty("supervison_input_marker");

    public string? GeneraroExe => _e.GetStringProperty("generator_exe");

    public IDictionary<string, string>? GeneratorEnv => _e.GetStringDictProperty("generator_env");

    public IEnumerable<string>? GeneratorOptions => _e.GetEnumerableNullableStringProperty("generator_options");

    public string? AnalyzerExe => _e.GetNullableStringProperty("analyzer_exe");

    public IDictionary<string, string>? AnalyzerEnv => _e.GetNullableStringDictProperty("analyzer_env");

    public IEnumerable<string>? AnalyzerOptions => _e.GetEnumerableNullableStringProperty("analyzer_options");

    public string? StatsFile => _e.GetNullableStringProperty("stats_file");

    public bool? RebootAfterSetup => _e.GetNullableBoolProperty("reboot_after_setup");

    public long? TargetTimeout => _e.GetNullableLongProperty("target_timeout");

    public long? EnsembleSyncDelay => _e.GetNullableLongProperty("ensemble_sync_delay");

    public bool? PreserveExistingOutputs => _e.GetNullableBoolProperty("preserve_existing_outputs");

    public IEnumerable<string>? ReportList => _e.GetEnumerableNullableStringProperty("report_list");

    public long? MinimizedStackDepth => _e.GetNullableLongProperty("minimized_stack_depth");

    public string? CoverageFilter => _e.GetNullableStringProperty("coverage_filter");

    public string? WaitForFiles => _e.GetNullableStringProperty("wait_for_files");
    public string? StatsFormat => _e.GetNullableStringProperty("stats_format");
}


public class TaskConfig : IFromJsonElement<TaskConfig> {
    readonly JsonElement _e;
    public TaskConfig(JsonElement e) => _e = e;
    public static TaskConfig Convert(JsonElement e) => new(e);

    public Guid JobId => _e.GetGuidProperty("job_id");
    public IEnumerable<Guid>? PrereqTasks => _e.GetEnumerableGuidProperty("prereq_tasks");
}

public class OneFuzzTask : IFromJsonElement<OneFuzzTask> {
    readonly JsonElement _e;

    public OneFuzzTask(JsonElement e) => _e = e;

    public static OneFuzzTask Convert(JsonElement e) => new(e);

    public Guid JobId => _e.GetGuidProperty("job_id");
    public Guid TaskId => _e.GetGuidProperty("task_id");
    public string State => _e.GetStringProperty("state");
    public string Os => _e.GetStringProperty("os");
    public Error? Error => _e.GetNullableObjectProperty<Error>("error");

    public DateTimeOffset? Heartbeat => _e.GetNullableDateTimeOffsetProperty("heartbeat");
    public DateTimeOffset? EndTime => _e.GetNullableDateTimeOffsetProperty("end_time");

    public Authentication? Auth => _e.GetNullableObjectProperty<Authentication>("auth");

    public UserInfo? UserInfo => _e.GetNullableObjectProperty<UserInfo>("user_info");

    public TaskConfig Config => _e.GetObjectProperty<TaskConfig>("task_config");
}


public class TaskApi : ApiBase {

    public TaskApi(Uri endpoint, Microsoft.OneFuzz.Service.Request request, ITestOutputHelper output) :
        base(endpoint, "/api/tasks", request, output) {
    }

    public static JsonObject TaskDetails(
        string taskType,
        long duration,
        string? targetExe = null,
        IDictionary<string, string>? targetEnv = null,
        IEnumerable<string>? targetOptions = null,
        long? targetWorkers = null,
        bool? targetOptionsMerge = null,
        bool? checkAsanLog = null,
        bool? checkDebugger = null,
        long? checkRetryCount = null,
        bool? checkFuzzerHelp = null,
        bool? expectCrashOnFailure = null,
        bool? renameOutput = null,
        string? supervisorExe = null,
        IDictionary<string, string>? supervisorEnv = null,
        IEnumerable<string>? supervisorOptions = null,
        string? supervisorInputMarker = null,
        string? generatorExe = null,
        IDictionary<string, string>? generatorEnv = null,
        IEnumerable<string>? generatorOptions = null,
        string? analyzerExe = null,
        IDictionary<string, string>? analyzerEnv = null,
        IEnumerable<string>? analyzerOptions = null,
        string? waitForFiles = null,
        string? statsFile = null,
        string? statsFormat = null,
        bool? rebootAfterSetup = null,
        long? targetTimeout = null,
        long? ensembleSyncDelay = null,
        bool? preserveExistingOutputs = null,
        IEnumerable<string>? reportList = null,
        long? minimizedStackDepth = null,
        string? coverageFilter = null
        ) {

        return new JsonObject()
            .AddV("task_type", taskType)
            .AddV("duration", duration)
            .AddIfNotNullV("target_exe", targetExe)
            .AddIfNotNullV("target_env", targetEnv)
            .AddIfNotNullV("target_options", targetOptions)
            .AddIfNotNullV("target_workers", targetWorkers)
            .AddIfNotNullV("target_options_merge", targetOptionsMerge)
            .AddIfNotNullV("check_asan_log", checkAsanLog)
            .AddIfNotNullV("check_debugger", checkDebugger)
            .AddIfNotNullV("check_retry_count", checkRetryCount)
            .AddIfNotNullV("check_fuzzer_help", checkFuzzerHelp)
            .AddIfNotNullV("expect_crash_on_failure", expectCrashOnFailure)
            .AddIfNotNullV("rename_output", renameOutput)
            .AddIfNotNullV("supervisor_exe", supervisorExe)
            .AddIfNotNullV("supervisor_env", supervisorEnv)
            .AddIfNotNullV("supervisor_options", supervisorOptions)
            .AddIfNotNullV("supervisor_input_marker", supervisorInputMarker)
            .AddIfNotNullV("generator_exe", generatorExe)
            .AddIfNotNullV("generator_env", generatorEnv)
            .AddIfNotNullV("generator_options", generatorOptions)
            .AddIfNotNullV("analyzer_exe", analyzerExe)
            .AddIfNotNullV("analyzer_env", analyzerEnv)
            .AddIfNotNullV("analyzer_options", analyzerOptions)
            .AddIfNotNullV("wait_for_files", waitForFiles)
            .AddIfNotNullV("stats_file", statsFile)
            .AddIfNotNullV("stats_format", statsFormat)
            .AddIfNotNullV("reboot_after_setup", rebootAfterSetup)
            .AddIfNotNullV("target_timeout", targetTimeout)
            .AddIfNotNullV("ensemble_sync_delay", ensembleSyncDelay)
            .AddIfNotNullV("preserve_existing_outputs", preserveExistingOutputs)
            .AddIfNotNullV("reprot_list", reportList)
            .AddIfNotNullV("minimized_stack_depth", minimizedStackDepth)
            .AddIfNotNullV("coverage_filter", coverageFilter);
    }


    public async Task<Result<IEnumerable<OneFuzzTask>, Error>> Get(Guid? jobId = null, Guid? taskId = null, List<string>? state = null) {
        var n = new JsonObject()
            .AddIfNotNullV("job_id", jobId)
            .AddIfNotNullV("task_id", taskId)
            .AddIfNotNullV("state", state);
        var r = await Get(n);
        return IEnumerableResult<OneFuzzTask>(r);
    }


    public async Task<Result<OneFuzzTask, Error>> Post(
        Guid jobId,
        JsonObject taskDetails,
        string taskPoolName,
        long taskPoolCount,
        IEnumerable<Guid>? prereqTasks = null,
        IEnumerable<(string, string)>? containers = null,
        IDictionary<string, string>? tags = null,
        bool? colocate = null
        ) {

        var j = new JsonObject()
            .AddV("job_id", jobId)
            .AddIfNotNullEnumerableV("prereq_tasks", prereqTasks)
            .AddIfNotNullEnumerableV("containers", containers)
            .AddIfNotNullV("tags", tags)
            .AddIfNotNullV("colocate", colocate)
            ;

        var pool = new JsonObject()
                .AddV("count", taskPoolCount)
                .AddV("pool_name", taskPoolName);

        j.Add("task", taskDetails);
        j.Add("pool", pool);

        var r = await Post(j);
        return Result<OneFuzzTask>(r);
    }

    public async Task<BooleanResult> Delete(Guid taskId) {
        var j = new JsonObject().AddV("task_id", taskId);

        var r = await Delete(j);
        return new BooleanResult(r);
    }
}
