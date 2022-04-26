using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IReproOperations : IStatefulOrm<Repro, VmState>
{
    public IAsyncEnumerable<Repro> SearchExpired();

    public System.Threading.Tasks.Task Stopping(Repro repro);

    public IAsyncEnumerable<Repro> SearchStates(IEnumerable<VmState>? States);

}

public class ReproOperations : StatefulOrm<Repro, VmState>, IReproOperations
{
    private static readonly Dictionary<Os, string> DEFAULT_OS = new Dictionary<Os, string>
    {
        {Os.Linux, "Canonical:UbuntuServer:18.04-LTS:latest"},
        {Os.Windows, "MicrosoftWindowsDesktop:Windows-10:20h2-pro:latest"}
    };

    const string DEFAULT_SKU = "Standard_DS1_v2";

    private IConfigOperations _configOperations;
    private ITaskOperations _taskOperations;

    private IVmOperations _vmOperations;

    private ICreds _creds;

    public ReproOperations(IStorage storage, ILogTracer log, IServiceConfig config, IConfigOperations configOperations, ITaskOperations taskOperations, ICreds creds, IVmOperations vmOperations)
        : base(storage, log, config)
    {
        _configOperations = configOperations;
        _taskOperations = taskOperations;
        _creds = creds;
        _vmOperations = vmOperations;
    }

    public IAsyncEnumerable<Repro> SearchExpired()
    {
        return QueryAsync(filter: $"end_time lt datetime'{DateTime.UtcNow.ToString("o")}'");
    }

    public async Async.Task<Vm> GetVm(Repro repro, InstanceConfig config)
    {
        var tags = config.VmTags;
        var task = await _taskOperations.GetByTaskId(repro.TaskId);
        if (task == null)
        {
            throw new Exception($"previous existing task missing: {repro.TaskId}");
        }

        var vmConfig = await _taskOperations.GetReproVmConfig(task);
        if (vmConfig == null)
        {
            if (!DEFAULT_OS.ContainsKey(task.Os))
            {
                throw new NotImplementedException($"unsupport OS for repro {task.Os}");
            }

            vmConfig = new TaskVm(
                _creds.GetBaseRegion(),
                DEFAULT_SKU,
                DEFAULT_OS[task.Os],
                null
            );
        }

        if (repro.Auth == null)
        {
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

    public async System.Threading.Tasks.Task Stopping(Repro repro)
    {
        var config = await _configOperations.Fetch();
        var vm = await GetVm(repro, config);
        if (!await _vmOperations.IsDeleted(vm))
        {
            _logTracer.Info($"vm stopping: {repro.VmId}");
            await _vmOperations.Delete(vm);
            await Replace(repro);
        }
        else
        {
            await Stopped(repro);
        }
    }

    public async Async.Task Stopped(Repro repro)
    {
        _logTracer.Info($"vm stopped: {repro.VmId}");
        await Delete(repro);
    }

    public IAsyncEnumerable<Repro> SearchStates(IEnumerable<VmState>? states)
    {
        string? queryString = null;
        if (states != null)
        {
            var statesString = String.Join(",", states);
            queryString = $"state in ({statesString})";
        }
        return QueryAsync(queryString);
    }
}
