using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;


public interface IConfig {
    Async.Task<TaskUnitConfig?> BuildTaskConfig(Job job, Task task);
    Task<ResultVoid<TaskConfigError>> CheckConfig(TaskConfig config);
}

public record TaskConfigError(string Error);

public class Config : IConfig {

    private readonly IOnefuzzContext _context;
    private readonly IContainers _containers;
    private readonly IServiceConfig _serviceConfig;
    private readonly ILogger _logTracer;
    private readonly IQueue _queue;

    public Config(ILogger<Config> logTracer, IOnefuzzContext context) {
        _context = context;
        _logTracer = logTracer;
        _containers = _context.Containers;
        _serviceConfig = _context.ServiceConfiguration;
        _queue = _context.Queue;
    }

    private static BlobContainerSasPermissions ConvertPermissions(ContainerPermission permission) {
        BlobContainerSasPermissions blobPermissions = 0;
        if (permission.HasFlag(ContainerPermission.Read)) {
            blobPermissions |= BlobContainerSasPermissions.Read;
        }

        if (permission.HasFlag(ContainerPermission.Write)) {
            blobPermissions |= BlobContainerSasPermissions.Write;
        }

        if (permission.HasFlag(ContainerPermission.Delete)) {
            blobPermissions |= BlobContainerSasPermissions.Delete;
        }

        if (permission.HasFlag(ContainerPermission.List)) {
            blobPermissions |= BlobContainerSasPermissions.List;
        }

        return blobPermissions;
    }

    public async Async.Task<TaskUnitConfig?> BuildTaskConfig(Job job, Task task) {

        if (!Defs.TASK_DEFINITIONS.ContainsKey(task.Config.Task.Type)) {
            throw new Exception($"unsupported task type: {task.Config.Task.Type}");
        }

        if (job.Config.Logs == null) {
            _logTracer.LogWarning("Missing log container {JobId} - {TaskId}", job.JobId, task.TaskId);
            return null;
        }

        var definition = Defs.TASK_DEFINITIONS[task.Config.Task.Type];

        var config = new TaskUnitConfig(
            InstanceId: await _containers.GetInstanceId(),
            JobId: job.JobId,
            TaskId: task.TaskId,
            logs: await _containers.AddContainerSasUrl(new Uri(job.Config.Logs)),
            TaskType: task.Config.Task.Type,
            InstanceTelemetryKey: _serviceConfig.ApplicationInsightsInstrumentationKey,
            MicrosoftTelemetryKey: _serviceConfig.OneFuzzTelemetry,
            HeartbeatQueue: await _queue.GetQueueSas("task-heartbeat", StorageType.Config, QueueSasPermissions.Add) ?? throw new Exception("unable to get heartbeat queue sas"),
            Tags: task.Config.Tags ?? new Dictionary<string, string>()
        );

        if (definition.MonitorQueue != null) {
            config.inputQueue = await _queue.GetQueueSas(task.TaskId.ToString(), StorageType.Corpus, QueueSasPermissions.All);
        }

        if (task.Config.Containers is not null) {
            var containersByType =
                await Async.Task.WhenAll(
                    definition.Containers
                    .Where(c => c.Type is not ContainerType.Setup)
                    .Select(async countainerDef => {
                        var syncedDirs =
                            await Async.Task.WhenAll(
                                task.Config.Containers
                                .Where(c => c.Type == countainerDef.Type)
                                .Select(async (container, i) =>
                                    new SyncedDir(
                                        string.Join("_", "task", countainerDef.Type.ToString().ToLower(), i),
                                        await _containers.GetContainerSasUrl(container.Name, StorageType.Corpus, ConvertPermissions(countainerDef.Permissions)))
                                ));

                        return (countainerDef, syncedDirs);
                    }));

            foreach (var (containerDef, syncedDirs) in containersByType) {
                if (!syncedDirs.Any()) {
                    continue;
                }

                IContainerDef def = containerDef switch {
                    ContainerDefinition { Compare: Compare.Equal or Compare.AtMost, Value: 1 }
                        when syncedDirs is [var syncedDir] => new SingleContainer(syncedDir),
                    _ => new MultipleContainer(syncedDirs)
                };

                switch (containerDef.Type) {
                    case ContainerType.Analysis:
                        config.Analysis = def;
                        break;
                    case ContainerType.Coverage:
                        config.Coverage = def;
                        break;
                    case ContainerType.Crashes:
                        config.Crashes = def;
                        break;
                    case ContainerType.Crashdumps:
                        config.Crashdumps = def;
                        break;
                    case ContainerType.Inputs:
                        config.Inputs = def;
                        break;
                    case ContainerType.NoRepro:
                        config.NoRepro = def;
                        break;
                    case ContainerType.ReadonlyInputs:
                        config.ReadonlyInputs = def;
                        break;
                    case ContainerType.Reports:
                        config.Reports = def;
                        break;
                    case ContainerType.Tools:
                        config.Tools = def;
                        break;
                    case ContainerType.UniqueInputs:
                        config.UniqueInputs = def;
                        break;
                    case ContainerType.UniqueReports:
                        config.UniqueReports = def;
                        break;
                    case ContainerType.RegressionReports:
                        config.RegressionReports = def;
                        break;
                    case ContainerType.ExtraSetup:
                        config.ExtraSetup = def;
                        break;
                    case ContainerType.ExtraOutput:
                        config.ExtraOutput = def;
                        break;
                    default:
                        throw new InvalidDataException($"unknown container type: {containerDef.Type}");
                }
            }
        }

        if (definition.Features.Contains(TaskFeature.SupervisorExe)) {
            config.SupervisorExe = task.Config.Task.SupervisorExe;
        }

        if (definition.Features.Contains(TaskFeature.SupervisorEnv)) {
            config.SupervisorEnv = task.Config.Task.SupervisorEnv ?? new Dictionary<string, string>();
        }

        if (definition.Features.Contains(TaskFeature.SupervisorOptions)) {
            config.SupervisorOptions = task.Config.Task.SupervisorOptions ?? new List<string>();
        }

        if (definition.Features.Contains(TaskFeature.SupervisorInputMarker)) {
            config.SupervisorInputMarker = task.Config.Task.SupervisorInputMarker;
        }

        if (definition.Features.Contains(TaskFeature.TargetExe)) {
            config.TargetExe = task.Config.Task.TargetExe;
        }

        if (definition.Features.Contains(TaskFeature.TargetExeOptional) && task.Config.Task.TargetExe != null) {
            config.TargetExe = task.Config.Task.TargetExe;
        }

        if (definition.Features.Contains(TaskFeature.TargetEnv)) {
            config.TargetEnv = task.Config.Task.TargetEnv ?? new Dictionary<string, string>();
        }

        if (definition.Features.Contains(TaskFeature.TargetOptions)) {
            config.TargetOptions = task.Config.Task.TargetOptions ?? new List<string>();
        }

        if (definition.Features.Contains(TaskFeature.TargetOptionsMerge)) {
            config.TargetOptionsMerge = task.Config.Task.TargetOptionsMerge ?? false;
        }

        if (definition.Features.Contains(TaskFeature.TargetWorkers)) {
            config.TargetWorkers = task.Config.Task.TargetWorkers;
        }

        if (definition.Features.Contains(TaskFeature.RenameOutput)) {
            config.RenameOutput = task.Config.Task.RenameOutput;
        }

        if (definition.Features.Contains(TaskFeature.GeneratorExe)) {
            config.GeneratorExe = task.Config.Task.GeneratorExe;
        }

        if (definition.Features.Contains(TaskFeature.GeneratorEnv)) {
            config.GeneratorEnv = task.Config.Task.GeneratorEnv ?? new Dictionary<string, string>();
        }

        if (definition.Features.Contains(TaskFeature.GeneratorOptions)) {
            config.GeneratorOptions = task.Config.Task.GeneratorOptions ?? new List<string>();
        }

        if (definition.Features.Contains(TaskFeature.WaitForFiles) && task.Config.Task.WaitForFiles != null) {
            config.WaitForFiles = task.Config.Task.WaitForFiles;
        }

        if (definition.Features.Contains(TaskFeature.AnalyzerExe)) {
            config.AnalyzerExe = task.Config.Task.AnalyzerExe;
        }

        if (definition.Features.Contains(TaskFeature.AnalyzerOptions)) {
            config.AnalyzerOptions = task.Config.Task.AnalyzerOptions ?? new List<string>();
        }

        if (definition.Features.Contains(TaskFeature.AnalyzerEnv)) {
            config.AnalyzerEnv = task.Config.Task.AnalyzerEnv ?? new Dictionary<string, string>();
        }

        if (definition.Features.Contains(TaskFeature.StatsFile)) {
            config.StatsFile = task.Config.Task.StatsFile;
            config.StatsFormat = task.Config.Task.StatsFormat;
        }

        if (definition.Features.Contains(TaskFeature.TargetTimeout)) {
            config.TargetTimeout = task.Config.Task.TargetTimeout;
        }

        if (definition.Features.Contains(TaskFeature.CheckAsanLog)) {
            config.CheckAsanLog = task.Config.Task.CheckAsanLog;
        }

        if (definition.Features.Contains(TaskFeature.CheckDebugger)) {
            config.CheckDebugger = task.Config.Task.CheckDebugger;
        }

        if (definition.Features.Contains(TaskFeature.CheckRetryCount)) {
            config.CheckRetryCount = task.Config.Task.CheckRetryCount ?? 0;
        }

        if (definition.Features.Contains(TaskFeature.EnsembleSyncDelay)) {
            config.EnsembleSyncDelay = task.Config.Task.EnsembleSyncDelay;
        }

        if (definition.Features.Contains(TaskFeature.CheckFuzzerHelp)) {
            config.CheckFuzzerHelp = task.Config.Task.CheckFuzzerHelp ?? true;
        }

        if (definition.Features.Contains(TaskFeature.PreserveExistingOutputs)) {
            config.PreserveExistingOutputs = task.Config.Task.PreserveExistingOutputs ?? false;
        }

        if (definition.Features.Contains(TaskFeature.ReportList)) {
            config.ReportList = task.Config.Task.ReportList;
        }

        if (definition.Features.Contains(TaskFeature.MinimizedStackDepth)) {
            config.MinimizedStackDepth = task.Config.Task.MinimizedStackDepth;
        }

        if (definition.Features.Contains(TaskFeature.ExpectCrashOnFailure)) {
            config.ExpectCrashOnFailure = task.Config.Task.ExpectCrashOnFailure ?? true;
        }

        if (definition.Features.Contains(TaskFeature.CoverageFilter)) {
            if (task.Config.Task.CoverageFilter != null) {
                config.CoverageFilter = task.Config.Task.CoverageFilter;
            }
        }

        if (definition.Features.Contains(TaskFeature.ModuleAllowlist)) {
            if (task.Config.Task.ModuleAllowlist != null) {
                config.ModuleAllowlist = task.Config.Task.ModuleAllowlist;
            }
        }

        if (definition.Features.Contains(TaskFeature.SourceAllowlist)) {
            if (task.Config.Task.SourceAllowlist != null) {
                config.SourceAllowlist = task.Config.Task.SourceAllowlist;
            }
        }

        if (definition.Features.Contains(TaskFeature.TargetAssembly)) {
            config.TargetAssembly = task.Config.Task.TargetAssembly;
        }

        if (definition.Features.Contains(TaskFeature.TargetClass)) {
            config.TargetClass = task.Config.Task.TargetClass;
        }

        if (definition.Features.Contains(TaskFeature.TargetMethod)) {
            config.TargetMethod = task.Config.Task.TargetMethod;
        }

        return config;
    }

    public async Async.Task<ResultVoid<TaskConfigError>> CheckConfig(TaskConfig config) {
        if (!Defs.TASK_DEFINITIONS.ContainsKey(config.Task.Type)) {
            return ResultVoid<TaskConfigError>.Error(new TaskConfigError($"unsupported task type: {config.Task.Type}"));
        }

        if (config.Vm != null && config.Pool != null) {
            return ResultVoid<TaskConfigError>.Error(new TaskConfigError($"either the vm or pool must be specified, but not both"));
        }

        var definition = Defs.TASK_DEFINITIONS[config.Task.Type];
        var r = await CheckContainers(definition, config);
        if (!r.IsOk) {
            return r;
        }

        if (definition.Features.Contains(TaskFeature.SupervisorExe) && config.Task.SupervisorExe == null) {
            var err = "missing supervisor_exe";
            _logTracer.LogError("{err}", err);
            return ResultVoid<TaskConfigError>.Error(new TaskConfigError(err));
        }

        if (definition.Features.Contains(TaskFeature.TargetMustUseInput) && !TargetUsesInput(config)) {
            return ResultVoid<TaskConfigError>.Error(new TaskConfigError("{input} must be used in target_env or target_options"));
        }

        if (config.Vm != null) {
            return ResultVoid<TaskConfigError>.Error(new TaskConfigError("specifying task config vm is no longer supported"));
        }

        if (config.Pool == null) {
            return ResultVoid<TaskConfigError>.Error(new TaskConfigError("pool must be specified"));
        }

        if (!CheckVal(definition.Vm.Compare, definition.Vm.Value, config.Pool!.Count)) {
            _logTracer.LogError("invalid vm count: expected {Comparison} {Expected} {Actual}", definition.Vm.Compare, definition.Vm.Value, config.Pool.Count);
            return ResultVoid<TaskConfigError>.Error(new TaskConfigError($"invalid vm count: expected {definition.Vm.Compare} {definition.Vm.Value}, actual {config.Pool.Count}"));
        }

        var pool = await _context.PoolOperations.GetByName(config.Pool.PoolName);
        if (!pool.IsOk) {
            return ResultVoid<TaskConfigError>.Error(new TaskConfigError($"invalid pool: {config.Pool.PoolName}"));
        }

        var checkTarget = await CheckTargetExe(config, definition);
        if (!checkTarget.IsOk) {
            return checkTarget;
        }

        if (definition.Features.Contains(TaskFeature.GeneratorExe)) {
            var container = config.Containers!.First(x => x.Type == ContainerType.Tools);

            if (config.Task.GeneratorExe == null) {
                return ResultVoid<TaskConfigError>.Error(new TaskConfigError($"generator_exe is not defined"));
            }

            var tool_paths = new[] { "{tools_dir}/", "{tools_dir}\\" };

            foreach (var toolPath in tool_paths) {
                if (config.Task.GeneratorExe.StartsWith(toolPath)) {
                    var generator = config.Task.GeneratorExe.Replace(toolPath, "");
                    if (!await _containers.BlobExists(container.Name, generator, StorageType.Corpus)) {
                        _logTracer.LogError("{GeneratorExe} does not exist in the tools `{Container}`", config.Task.GeneratorExe, container.Name);
                        return ResultVoid<TaskConfigError>.Error(new TaskConfigError($"generator_exe `{config.Task.GeneratorExe}` does not exist in the tools container `{container.Name}`"));
                    }
                }
            }
        }

        if (definition.Features.Contains(TaskFeature.StatsFile)) {
            if (config.Task.StatsFile != null && config.Task.StatsFormat == null) {
                var err2 = "using a stats_file requires a stats_format";
                _logTracer.LogError("{err2}", err2);
                return ResultVoid<TaskConfigError>.Error(new TaskConfigError(err2));
            }
        }

        return ResultVoid<TaskConfigError>.Ok();

    }

    private async Task<ResultVoid<TaskConfigError>> CheckTargetExe(TaskConfig config, TaskDefinition definition) {
        if (config.Task.TargetExe == null) {
            if (definition.Features.Contains(TaskFeature.TargetExe)) {
                return ResultVoid<TaskConfigError>.Error(new TaskConfigError("missing target_exe"));
            }

            if (definition.Features.Contains(TaskFeature.TargetExeOptional)) {
                return ResultVoid<TaskConfigError>.Ok();
            }
            return ResultVoid<TaskConfigError>.Ok();
        }

        // User-submitted paths must be relative to the setup directory that contains them.
        // They also must be normalized, and exclude special filesystem path elements.
        //
        // For example, accessing the blob store path "./foo" generates an exception, but
        // "foo" and "foo/bar" do not.

        if (!IsValidBlobName(config.Task.TargetExe)) {
            return ResultVoid<TaskConfigError>.Error(new TaskConfigError("target_exe must be a canonicalized relative path"));
        }


        var container = config.Containers!.FirstOrDefault(x => x.Type == ContainerType.Setup);
        if (container != null) {
            if (!await _containers.BlobExists(container.Name, config.Task.TargetExe, StorageType.Corpus)) {
                _logTracer.LogWarning("target_exe `{TargetExe}` does not exist in the setup container `{Container}`", config.Task.TargetExe, container.Name);
            }
        }

        return ResultVoid<TaskConfigError>.Ok();
    }



    // Azure Blob Storage uses a flat scheme, and has no true directory hierarchy. Forward
    // slashes are used to delimit a _virtual_ directory structure.
    private static bool IsValidBlobName(string blobName) {
        // https://docs.microsoft.com/en-us/rest/api/storageservices/naming-and-referencing-containers--blobs--and-metadata#blob-names
        const int MIN_LENGTH = 1;
        const int MAX_LENGTH = 1024; // inclusive
        const int MAX_PATH_SEGMENTS = 254;

        var length = blobName.Length;

        // No leading/trailing whitespace.
        if (blobName != blobName.Trim()) {
            return false;
        }

        if (length < MIN_LENGTH) {
            return false;
        }

        if (length > MAX_LENGTH) {
            return false;
        }

        var segments = blobName.Split(new[] { Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar });

        if (segments.Length > MAX_PATH_SEGMENTS) {
            return false;
        }

        // No path segment should end with a dot (`.`).
        if (segments.Any(s => s.EndsWith('.'))) {
            return false;
        }

        // Reject absolute paths to avoid confusion.
        if (Path.IsPathRooted(blobName)) {
            return false;
        }

        // Reject paths with special relative filesystem entries.
        if (segments.Contains(".")) {
            return false;
        }

        if (segments.Contains("..")) {
            return false;
        }

        return true;
    }

    private static bool TargetUsesInput(TaskConfig config) {
        if (config.Task.TargetOptions != null) {
            if (config.Task.TargetOptions.Any(x => x.Contains("{input}")))
                return true;
        }

        if (config.Task.TargetEnv != null) {
            if (config.Task.TargetEnv.Values.Any(x => x.Contains("{input}")))
                return true;
        }
        return false;
    }

    private async Task<ResultVoid<TaskConfigError>> CheckContainers(TaskDefinition definition, TaskConfig config) {

        if (config.Containers == null) {
            return ResultVoid<TaskConfigError>.Ok();
        }

        var exist = new HashSet<Container>();
        var containers = new Dictionary<ContainerType, List<Container>>();

        foreach (var container in config.Containers) {
            if (!exist.Add(container.Name)) {
                continue;
            }

            if (await _containers.FindContainer(container.Name, StorageType.Corpus) == null) {
                return ResultVoid<TaskConfigError>.Error(new TaskConfigError($"missing container: {container.Name}"));
            }

            if (!containers.ContainsKey(container.Type)) {
                containers.Add(container.Type, new List<Container>());
            }

            containers[container.Type].Add(container.Name);
        }

        foreach (var containerDef in definition.Containers) {
            var r = CheckContainer(containerDef.Compare, containerDef.Value, containerDef.Type, containers);
            if (!r.IsOk) {
                return r;
            }
        }

        var containerTypes = definition.Containers.Select(x => x.Type).ToHashSet();
        var missing = containers.Keys.Where(x => !containerTypes.Contains(x)).ToList();
        if (missing.Any()) {
            var types = string.Join(", ", missing);
            return ResultVoid<TaskConfigError>.Error(new TaskConfigError($"unsupported container types for this task: {types}"));
        }

        if (definition.MonitorQueue != null) {
            if (!containerTypes.Contains(definition.MonitorQueue.Value)) {
                return ResultVoid<TaskConfigError>.Error(new TaskConfigError($"unable to monitor container type as it is not used by this task: {definition.MonitorQueue}"));
            }
        }

        return ResultVoid<TaskConfigError>.Ok();
    }

    private static ResultVoid<TaskConfigError> CheckContainer(Compare compare, long expected, ContainerType containerType, Dictionary<ContainerType, List<Container>> containers) {
        var actual = containers.TryGetValue(containerType, out var v) ? v.Count : 0;
        if (!CheckVal(compare, expected, actual)) {
            return ResultVoid<TaskConfigError>.Error(
                new TaskConfigError($"container type {containerType}: expected {compare} {expected}, got {actual}"));
        }

        return ResultVoid<TaskConfigError>.Ok();
    }

    private static bool CheckVal(Compare compare, long expected, long actual) {
        return compare switch {
            Compare.Equal => expected == actual,
            Compare.AtLeast => expected <= actual,
            Compare.AtMost => expected >= actual,
            _ => throw new NotSupportedException()
        };
    }
}
