using Azure.Storage.Sas;

namespace Microsoft.OneFuzz.Service;



public interface IConfig {
    string GetSetupContainer(TaskConfig config);
    TaskConfig BuildTaskConfig(Job job, Task task);
}

public class Config : IConfig {

    private readonly IContainers _containers;
    private readonly IServiceConfig _serviceConfig;

    private readonly IQueue _queue;

    public Config(IContainers containers, IServiceConfig serviceConfig, IQueue queue) {
        _containers = containers;
        _serviceConfig = serviceConfig;
        _queue = queue;
    }

    private BlobContainerSasPermissions ConvertPermissions(ContainerPermission permission) {
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

    public async Async.Task<TaskConfig> BuildTaskConfig(Job job, Task task) {

        if (!Defs.TASK_DEFINITIONS.ContainsKey(task.Config.Task.Type)) {
            throw new Exception($"unsupported task type: {task.Config.Task.Type}");
        }

        if (job.Config.Logs == null) {
            throw new Exception($"Missing log container:  job_id {job.JobId}, task_id {task.TaskId}");
        }

        var definition = Defs.TASK_DEFINITIONS[task.Config.Task.Type];

        var config = new TaskUnitConfig(
            InstanceId: await _containers.GetInstanceId(),
            JobId: job.JobId,
            TaskId: task.TaskId,
            logs: _containers.AddContainerSasUrl(new Uri(job.Config.Logs)),
            TaskType: task.Config.Task.Type,
            InstanceTelemetryKey: _serviceConfig.ApplicationInsightsInstrumentationKey,
            MicrosoftTelemetryKey: _serviceConfig.OneFuzzTelemetry,
            HeartbeatQueue: _queue.GetQueueSas("task-heartbeat", StorageType.Config, QueueSasPermissions.Add) ?? throw new Exception("unable to get heartbeat queue sas")
        );

        if (definition.MonitorQueue != null) {
            config.inputQueue = _queue.GetQueueSas(task.TaskId.ToString(), StorageType.Config, QueueSasPermissions.Add | QueueSasPermissions.Read | QueueSasPermissions.Update | QueueSasPermissions.Process);
        }

        var containersByType = definition.Containers.Where(c => c.Type != ContainerType.Setup && task.Config.Containers != null)
            .ToAsyncEnumerable()
            .SelectAwait(async countainerDef => {
                var containers = await
                    task.Config.Containers!
                        .Where(c => c.Type == countainerDef.Type).Select(container => (countainerDef, container))
                        .Where(x => x.container != null)
                        .ToAsyncEnumerable()
                        .SelectAwait(async (x, i) =>
                                new SyncedDir(
                                    string.Join("_", "task", x.Item1.Type.ToString().ToLower(), i),
                                    await _containers.GetContainerSasUrl(x.Item2.Name, StorageType.Corpus, ConvertPermissions(x.Item1.Permissions)))
                        ).ToListAsync();
                return (countainerDef, containers);
            }
                );

        await foreach (var data in containersByType) {

            IContainerDef def = data.countainerDef switch {
                ContainerDefinition { Compare: Compare.Equal, Value: 1 } or
                ContainerDefinition { Compare: Compare.AtMost, Value: 1 } => new SingleContainer(data.containers[0]),
                _ => new MultipleContainer(data.containers)
            };

            switch (data.countainerDef.Type) {
                case ContainerType.Analysis:
                    config.Analysis = def;
                    break;
                case ContainerType.Coverage:
                    config.Coverage = def;
                    break;
                case ContainerType.Crashes:
                    config.Crashes = def;
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
            config.TargetExe = $"setup/{task.Config.Task.TargetExe}";
        }

        if (definition.Features.Contains(TaskFeature.TargetExeOptional) && config.TargetExe != null) {
            config.TargetExe = $"setup/{task.Config.Task.TargetExe}";
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





        throw new NotImplementedException();
    }

    public string GetSetupContainer(TaskConfig config) {

        foreach (var container in config.Containers ?? throw new Exception("Missing containers")) {
            if (container.Type == ContainerType.Setup) {
                return container.Name.ContainerName;
            }
        }

        throw new Exception($"task missing setup container: task_type = {config.Task.Type}");
    }

    TaskConfig IConfig.BuildTaskConfig(Job job, Task task) {
        throw new NotImplementedException();
    }
}
