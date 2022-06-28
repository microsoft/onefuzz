using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IReproOperations : IStatefulOrm<Repro, VmState> {
    public IAsyncEnumerable<Repro> SearchExpired();

    public Async.Task<Repro> Stopping(Repro repro);

    public IAsyncEnumerable<Repro> SearchStates(IEnumerable<VmState>? States);
}

public class ReproOperations : StatefulOrm<Repro, VmState, ReproOperations>, IReproOperations {
    private static readonly Dictionary<Os, string> DEFAULT_OS = new Dictionary<Os, string>
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
                throw new NotImplementedException($"unsupport OS for repro {task.Os}");
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
            queryString = string.Join(
                " or ",
                states.Select(s => $"state eq '{s}'")
            );
        }
        return QueryAsync(queryString);
    }
}
