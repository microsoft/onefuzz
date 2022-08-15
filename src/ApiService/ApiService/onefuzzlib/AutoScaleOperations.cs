using System.Threading.Tasks;
using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IAutoScaleOperations {
    public Task<ResultVoid<(int, string)>> Insert(AutoScale autoScale);
    public Task<AutoScale> GetSettingsForScaleset(Guid scalesetId);
}

public class AutoScaleOperations : Orm<AutoScale>, IAutoScaleOperations {
    public AutoScaleOperations(ILogTracer logTracer, IOnefuzzContext context)
        : base(logTracer, context) { }

    public async Task<AutoScale> GetSettingsForScaleset(Guid scalesetId)
        => await QueryAsync(Query.PartitionKey(scalesetId.ToString())).SingleAsync();
}
