using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IReproOperations : IStatefulOrm<Repro, VmState> {
    public IAsyncEnumerable<Repro> SearchExpired();

    public Async.Task<Repro> Stopping(Repro repro);

    public IAsyncEnumerable<Repro> SearchStates(IEnumerable<VmState>? states);
    Task<OneFuzzResult<Repro>> Create(ReproConfig config, UserInfo userInfo);
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
