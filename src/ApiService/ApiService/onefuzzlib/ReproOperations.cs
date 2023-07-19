using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;
using Azure.ResourceManager.Compute.Models;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;

public interface IReproOperations : IStatefulOrm<Repro, VmState> {
    public IAsyncEnumerable<Repro> SearchExpired();

    public IAsyncEnumerable<Repro> SearchStates(IEnumerable<VmState>? states);

    public Async.Task<Repro> SetFailed(Repro repro, VirtualMachineInstanceView instanceView);

    public Async.Task<Repro> SetError(Repro repro, Error result);

    public Async.Task<OneFuzzResultVoid> BuildReproScript(Repro repro);

    public Async.Task<Container?> GetSetupContainer(Repro repro);
    Task<OneFuzzResult<Repro>> Create(ReproConfig config, UserInfo userInfo);

    // state transitions:
    Task<Repro> Init(Repro repro);
    Task<Repro> ExtensionsLaunch(Repro repro);
    Task<Repro> ExtensionsFailed(Repro repro);
    Task<Repro> VmAllocationFailed(Repro repro);
    Task<Repro> Running(Repro repro);
    Task<Repro> Stopping(Repro repro);
    Task<Repro> Stopped(Repro repro);
}

public class ReproOperations : StatefulOrm<Repro, VmState, ReproOperations>, IReproOperations {
    const string DEFAULT_SKU = "Standard_DS1_v2";

    public ReproOperations(ILogger<ReproOperations> log, IOnefuzzContext context)
        : base(log, context) {

    }

    public IAsyncEnumerable<Repro> SearchExpired() {
        return QueryAsync(filter: Query.OlderThan("end_time", DateTimeOffset.UtcNow));
    }

    public async Async.Task<Vm> GetVm(Repro repro, InstanceConfig config) {
        var taskOperations = _context.TaskOperations;
        var tags = config.VmTags;
        var task = await taskOperations.GetByTaskId(repro.TaskId);
        if (task == null) {
            throw new Exception($"previous existing task missing: {repro.TaskId}");
        }

        Dictionary<Os, ImageReference> default_os = new()
        {
            { Os.Linux, config.DefaultLinuxVmImage ?? DefaultImages.Linux },
            { Os.Windows, config.DefaultWindowsVmImage ?? DefaultImages.Windows },
        };

        var vmConfig = await taskOperations.GetReproVmConfig(task);
        if (vmConfig == null) {
            if (!default_os.ContainsKey(task.Os)) {
                throw new NotSupportedException($"unsupport OS for repro {task.Os}");
            }

            vmConfig = new TaskVm(
                await _context.Creds.GetBaseRegion(),
                DEFAULT_SKU,
                default_os[task.Os],
                null
            );
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
            _logTracer.LogInformation("vm stopping: {VmId} {VmName}", repro.VmId, vm.Name);
            var rr = await vmOperations.Delete(vm);
            if (rr) {
                _logTracer.LogInformation("repro vm fully deleted {VmId} {VmName}", repro.VmId, vm.Name);
            }
            repro = repro with { State = VmState.Stopping };
            var r = await Replace(repro);
            if (!r.IsOk) {
                _logTracer.AddHttpStatus(r.ErrorV);
                _logTracer.LogError("failed to replace repro {VmId} {VmName} marked Stopping", repro.VmId, vm.Name);
            }
            return repro;
        } else {
            return await Stopped(repro);
        }
    }

    public async Async.Task<Repro> Stopped(Repro repro) {
        _logTracer.LogInformation("vm stopped: {VmId}", repro.VmId);
        // BUG?: why are we updating repro and then deleting it and returning a new value
        repro = repro with { State = VmState.Stopped };
        var r = await Delete(repro);
        if (!r.IsOk && r.ErrorV.Status != HttpStatusCode.NotFound) {
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("failed to delete repro {VmId} marked as stopped", repro.VmId);
        }

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
            if (vmData.ProvisioningState == "Failed") {
                var failedVmData = await _context.VmOperations.GetVmWithInstanceView(vm.Name);
                if (failedVmData is null) {
                    // this should exist since we just loaded the VM above
                    throw new InvalidOperationException("Unable to fetch instance-view data for VM");
                }

                return await _context.ReproOperations.SetFailed(repro, failedVmData.InstanceView);
            } else {
                var scriptResult = await BuildReproScript(repro);
                if (!scriptResult.IsOk) {
                    return await _context.ReproOperations.SetError(repro, scriptResult.ErrorV);
                }
                repro = repro with { State = VmState.ExtensionsLaunch };
            }
        } else {
            var nsg = Nsg.ForRegion(vm.Region);
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

        var r = await Replace(repro);
        if (!r.IsOk) {
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("failed to replace init repro: {VmId}", repro.VmId);
        }
        return repro;
    }

    public async Async.Task<Repro> ExtensionsLaunch(Repro repro) {
        var config = await _context.ConfigOperations.Fetch();
        var vm = await GetVm(repro, config);
        var vmData = await _context.VmOperations.GetVm(vm.Name);
        if (vmData == null) {
            return await _context.ReproOperations.SetError(
                repro,
                Error.Create(
                    ErrorCode.VM_CREATE_FAILED,
                    "failed before launching extensions"));
        }
        if (vmData.ProvisioningState == "Failed") {
            var failedVmData = await _context.VmOperations.GetVmWithInstanceView(vm.Name);
            if (failedVmData is null) {
                // this should exist since we loaded the VM above
                throw new InvalidOperationException("Unable to find instance-view data fro VM");
            }

            return await _context.ReproOperations.SetFailed(repro, failedVmData.InstanceView);
        }

        if (string.IsNullOrEmpty(repro.Ip)) {
            repro = repro with {
                Ip = await _context.IpOperations.GetPublicIp(vmData.NetworkProfile.NetworkInterfaces.First().Id)
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
        }

        if (result.OkV) {
            // this means extensions are all completed - transition to Running state
            repro = repro with { State = VmState.Running };
            await Replace(repro).IgnoreResult();
        }

        return repro;
    }

    public async Async.Task<Repro> SetFailed(Repro repro, VirtualMachineInstanceView instanceView) {
        var errors = instanceView.Statuses
            .Where(status => status.Level.HasValue && string.Equals(status.Level?.ToString(), "error", StringComparison.OrdinalIgnoreCase))
            .Select(status => $"{status.Code} {status.DisplayStatus} {status.Message}")
            .ToArray();

        return await SetError(
            repro,
            new Error(
                ErrorCode.VM_CREATE_FAILED,
                errors.ToList()));
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
        var auth = await _context.SecretsOperations.GetSecretValue(repro.Auth);

        if (auth == null) {
            return OneFuzzResultVoid.Error(ErrorCode.VM_CREATE_FAILED, "unable to fetch auth secret");
        }

        switch (task.Os) {
            case Os.Windows:
                var sshPath = "$env:ProgramData/ssh/administrators_authorized_keys";
                var cmds = new List<string>()
                {
                    $"Set-Content -Path {sshPath} -Value \"{auth.PublicKey}\"",
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
                WellKnownContainers.ReproScripts,
                $"{repro.VmId}/{fileName}",
                fileContents,
                StorageType.Config
            );
        }

        _logTracer.LogInformation("saved repro script {VmId}", repro.VmId);
        return OneFuzzResultVoid.Ok;
    }

    public async Async.Task<Repro> SetError(Repro repro, Error result) {
        _logTracer.LogInformation(
            "repro failed: {VmId} - {TaskId} {Error}",
            repro.VmId,
            repro.TaskId,
            result
        );

        repro = repro with {
            Error = result,
            State = VmState.Stopping
        };

        var r = await Replace(repro);
        if (!r.IsOk) {
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("failed to replace repro record for {VmId}", repro.VmId);
        }
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
        if (reportOrRegression is not Report report) {
            return OneFuzzResult<Repro>.Error(ErrorCode.UNABLE_TO_FIND, "unable to find report");
        }

        var task = await _context.TaskOperations.GetByTaskId(report.TaskId);
        if (task is null) {
            return OneFuzzResult<Repro>.Error(ErrorCode.INVALID_REQUEST, "unable to find task");
        }

        var auth = await _context.SecretsOperations.StoreSecret(new SecretValue<Authentication>(await AuthHelpers.BuildAuth(_logTracer)));

        var vm = new Repro(
            VmId: Guid.NewGuid(),
            Config: config,
            TaskId: task.TaskId,
            Os: task.Os,
            Auth: new SecretAddress<Authentication>(auth),
            EndTime: DateTimeOffset.UtcNow + TimeSpan.FromHours(config.Duration),
            UserInfo: new(ObjectId: userInfo.ObjectId, ApplicationId: userInfo.ApplicationId));

        var r = await _context.ReproOperations.Insert(vm);
        if (!r.IsOk) {
            _logTracer.AddHttpStatus(r.ErrorV);
            _logTracer.LogError("failed to insert repro record for {VmId}", vm.VmId);
            return OneFuzzResult<Repro>.Error(
                ErrorCode.UNABLE_TO_CREATE,
                new[] { "failed to insert repro record" });
        }

        return OneFuzzResult.Ok(vm);
    }

    public Task<Repro> ExtensionsFailed(Repro repro) {
        // nothing to do
        return Async.Task.FromResult(repro);
    }

    public Task<Repro> VmAllocationFailed(Repro repro) {
        // nothing to do
        return Async.Task.FromResult(repro);
    }

    public Task<Repro> Running(Repro repro) {
        // nothing to do
        return Async.Task.FromResult(repro);
    }
}
