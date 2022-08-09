using System.Globalization;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure.ResourceManager.Compute;

namespace Microsoft.OneFuzz.Service;

public interface IReproOperations : IStatefulOrm<Repro, VmState> {
    public IAsyncEnumerable<Repro> SearchExpired();

    public Async.Task<Repro> Stopping(Repro repro);

    public IAsyncEnumerable<Repro> SearchStates(IEnumerable<VmState>? states);


    public Async.Task<Repro> Init(Repro repro);
    public Async.Task<Repro> ExtensionsLaunch(Repro repro);

    public Async.Task<Repro> Stopped(Repro repro);

    public Async.Task<Repro> SetFailed(Repro repro, VirtualMachineResource vmData);

    public Async.Task<Repro> SetError(Repro repro, Error result);

    public Async.Task<OneFuzzResultVoid> BuildReproScript(Repro repro);

    public Async.Task<Container?> GetSetupContainer(Repro repro);
    Task<OneFuzzResult<Repro>> Create(ReproConfig config, UserInfo userInfo);
}

public class ReproOperations : StatefulOrm<Repro, VmState, ReproOperations>, IReproOperations {
    private static readonly Dictionary<Os, string> DEFAULT_OS = new()
    {
        {Os.Linux, "Canonical:UbuntuServer:18.04-LTS:latest"},
        {Os.Windows, "MicrosoftWindowsDesktop:Windows-10:20h2-pro:latest"}
    };

    const string DEFAULT_SKU = "Standard_DS1_v2";



    public ReproOperations(ILogTracer log, IOnefuzzContext context)
        : base(log, context) {

    }

    public IAsyncEnumerable<Repro> SearchExpired() {
        return QueryAsync(filter: $"end_time lt datetime'{DateTime.UtcNow.ToString("o")}'");
    }

    public async Async.Task<Vm> GetVm(Repro repro, InstanceConfig config) {
        var taskOperations = _context.TaskOperations;
        var tags = config.VmTags;
        var task = await taskOperations.GetByTaskId(repro.TaskId);
        if (task == null) {
            throw new Exception($"previous existing task missing: {repro.TaskId}");
        }

        var vmConfig = await taskOperations.GetReproVmConfig(task);
        if (vmConfig == null) {
            if (!DEFAULT_OS.ContainsKey(task.Os)) {
                throw new NotSupportedException($"unsupport OS for repro {task.Os}");
            }

            vmConfig = new TaskVm(
                await _context.Creds.GetBaseRegion(),
                DEFAULT_SKU,
                DEFAULT_OS[task.Os],
                null
            );
        }

        if (repro.Auth == null) {
            throw new Exception("missing auth");
        }

        return new Vm(
            repro.VmId.ToString(),
            vmConfig.Region,
            vmConfig.Sku,
            vmConfig.Image,
            repro.Auth,
            null,
            tags
        );
    }

    public async Async.Task<Repro> Stopping(Repro repro) {
        var config = await _context.ConfigOperations.Fetch();
        var vm = await GetVm(repro, config);
        var vmOperations = _context.VmOperations;
        if (!await vmOperations.IsDeleted(vm)) {
            _logTracer.Info($"vm stopping: {repro.VmId}");
            await vmOperations.Delete(vm);
            repro = repro with { State = VmState.Stopping };
            await Replace(repro);
            return repro;
        } else {
            return await Stopped(repro);
        }
    }

    public async Async.Task<Repro> Stopped(Repro repro) {
        _logTracer.Info($"vm stopped: {repro.VmId}");
        repro = repro with { State = VmState.Stopped };
        await Delete(repro);
        return repro;
    }

    public IAsyncEnumerable<Repro> SearchStates(IEnumerable<VmState>? states) {
        string? queryString = null;
        if (states != null) {
            queryString = Query.EqualAnyEnum("state", states);
        }
        return QueryAsync(queryString);
    }

    public async Async.Task<Repro> Init(Repro repro) {
        var config = await _context.ConfigOperations.Fetch();
        var vm = await GetVm(repro, config);
        var vmData = await _context.VmOperations.GetVm(vm.Name);
        if (vmData != null) {
            if (vmData.Data.ProvisioningState == "Failed") {
                return await _context.ReproOperations.SetFailed(repro, vmData);
            } else {
                var scriptResult = await BuildReproScript(repro);
                if (!scriptResult.IsOk) {
                    return await _context.ReproOperations.SetError(repro, scriptResult.ErrorV);
                }
                repro = repro with { State = VmState.ExtensionsLaunch };
            }
        } else {
            var nsg = new Nsg(vm.Region, vm.Region);
            var result = await _context.NsgOperations.Create(nsg);
            if (!result.IsOk) {
                return await _context.ReproOperations.SetError(repro, result.ErrorV);
            }

            var nsgConfig = config.ProxyNsgConfig;
            result = await _context.NsgOperations.SetAllowedSources(nsg, nsgConfig);
            if (!result.IsOk) {
                return await _context.ReproOperations.SetError(repro, result.ErrorV);
            }

            vm = vm with { Nsg = nsg };
            result = await _context.VmOperations.Create(vm);
            if (!result.IsOk) {
                return await _context.ReproOperations.SetError(repro, result.ErrorV);
            }
        }

        await Replace(repro);
        return repro;
    }

    public async Async.Task<Repro> ExtensionsLaunch(Repro repro) {
        var config = await _context.ConfigOperations.Fetch();
        var vm = await GetVm(repro, config);
        var vmData = await _context.VmOperations.GetVm(vm.Name);
        if (vmData == null) {
            return await _context.ReproOperations.SetError(
                repro,
                OneFuzzResultVoid.Error(
                    ErrorCode.VM_CREATE_FAILED,
                    "failed before launching extensions"
                ).ErrorV
            );
        }

        if (vmData.Data.ProvisioningState == "Failed") {
            return await _context.ReproOperations.SetFailed(repro, vmData);
        }

        if (string.IsNullOrEmpty(repro.Ip)) {
            repro = repro with {
                Ip = await _context.IpOperations.GetPublicIp(vmData.Data.NetworkProfile.NetworkInterfaces.First().Id)
            };
        }

        var extensions = await _context.Extensions.ReproExtensions(
            vm.Region,
            repro.Os,
            repro.VmId,
            repro.Config,
            await _context.ReproOperations.GetSetupContainer(repro)
        );

        var result = await _context.VmOperations.AddExtensions(vm, extensions);
        if (!result.IsOk) {
            return await SetError(repro, result.ErrorV);
        } else {
            repro = repro with { State = VmState.Running };
        }

        await Replace(repro);
        return repro;
    }

    public async Async.Task<Repro> SetFailed(Repro repro, VirtualMachineResource vmData) {
        var errors = (await vmData.InstanceViewAsync()).Value.Statuses
            .Where(status => status.Level.HasValue && string.Equals(status.Level?.ToString(), "error", StringComparison.OrdinalIgnoreCase))
            .Select(status => $"{status.Code} {status.DisplayStatus} {status.Message}")
            .ToArray();

        return await SetError(repro, OneFuzzResultVoid.Error(
            ErrorCode.VM_CREATE_FAILED,
            errors
        ).ErrorV);
    }

    public async Task<OneFuzzResultVoid> BuildReproScript(Repro repro) {
        if (repro.Auth == null) {
            return OneFuzzResultVoid.Error(
                ErrorCode.VM_CREATE_FAILED,
                "missing auth"
            );
        }

        var task = await _context.TaskOperations.GetByTaskId(repro.TaskId);
        if (task == null) {
            return OneFuzzResultVoid.Error(
                ErrorCode.VM_CREATE_FAILED,
                $"unable to find task with id: {repro.TaskId}"
            );
        }

        var report = await _context.Reports.GetReport(repro.Config.Container, repro.Config.Path);
        if (report == null) {
            return OneFuzzResultVoid.Error(
                ErrorCode.VM_CREATE_FAILED,
                "unable to perform repro for crash reports without inputs"
            );
        }

        var files = new Dictionary<string, string>();
        switch (task.Os) {
            case Os.Windows:
                var sshPath = "$env:ProgramData/ssh/administrators_authorized_keys";
                var cmds = new List<string>()
                {
                    $"Set-Content -Path {sshPath} -Value \"{repro.Auth.PublicKey}\"",
                    ". C:\\onefuzz\\tools\\win64\\onefuzz.ps1",
                    "Set-SetSSHACL",
                    $"while (1) {{ cdb -server tcp:port=1337 -c \"g\" setup\\{task.Config.Task.TargetExe} {report?.InputBlob?.Name} }}"
                };
                var winCmd = string.Join("\r\n", cmds);
                files.Add("repro.ps1", winCmd);
                break;
            case Os.Linux:
                var gdbFmt = "ASAN_OPTIONS='abort_on_error=1' gdbserver {0} /onefuzz/setup/{1} /onefuzz/downloaded/{2}";
                var linuxCmd = $"while :; do {string.Format(CultureInfo.InvariantCulture, gdbFmt, "localhost:1337", task.Config.Task.TargetExe, report?.InputBlob?.Name)}; done";
                files.Add("repro.sh", linuxCmd);

                var linuxCmdStdOut = $"#!/bin/bash\n{string.Format(CultureInfo.InvariantCulture, gdbFmt, "-", task.Config.Task.TargetExe, report?.InputBlob?.Name)}";
                files.Add("repro-stdout.sh", linuxCmdStdOut);
                break;
            default: throw new NotSupportedException($"invalid task os: {task.Os}");
        }

        foreach (var (fileName, fileContents) in files) {
            await _context.Containers.SaveBlob(
                new Container("repro-scripts"),
                $"{repro.VmId}/{fileName}",
                fileContents,
                StorageType.Config
            );
        }

        _logTracer.Info("saved repro script");
        return OneFuzzResultVoid.Ok;
    }

    public async Async.Task<Repro> SetError(Repro repro, Error result) {
        _logTracer.Error(
            $"repro failed: vm_id: {repro.VmId} task_id: {repro.TaskId} error: {result}"
        );

        repro = repro with {
            Error = result,
            State = VmState.Stopping
        };

        await Replace(repro);
        return repro;
    }

    public async Task<Container?> GetSetupContainer(Repro repro) {
        var task = await _context.TaskOperations.GetByTaskId(repro.TaskId);
        return task?.Config?.Containers?
            .Where(container => container.Type == ContainerType.Setup)
            .FirstOrDefault()?
            .Name;
    }

    public async Task<OneFuzzResult<Repro>> Create(ReproConfig config, UserInfo userInfo) {
        var reportOrRegression = await _context.Reports.GetReportOrRegression(config.Container, config.Path);
        if (reportOrRegression is Report report) {
            var task = await _context.TaskOperations.GetByTaskId(report.TaskId);
            if (task == null) {
                return OneFuzzResult<Repro>.Error(ErrorCode.INVALID_REQUEST, "unable to find task");
            }

            var vm = new Repro(
                VmId: Guid.NewGuid(),
                Config: config,
                TaskId: task.TaskId,
                Os: task.Os,
                Auth: Auth.BuildAuth(),
                EndTime: DateTimeOffset.UtcNow + TimeSpan.FromHours(config.Duration),
                UserInfo: userInfo
            );

            await _context.ReproOperations.Insert(vm);
            return OneFuzzResult<Repro>.Ok(vm);
        } else {
            return OneFuzzResult<Repro>.Error(ErrorCode.UNABLE_TO_FIND, "unable to find report");
        }
    }
}
