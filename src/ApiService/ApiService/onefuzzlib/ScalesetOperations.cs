using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IScalesetOperations : IOrm<Scaleset>
{
    IAsyncEnumerable<Scaleset> Search();
}

public class ScalesetOperations : StatefulOrm<Scaleset, ScalesetState>, IScalesetOperations
{

    public ScalesetOperations(IStorage storage, ILogTracer log, IServiceConfig config)
        : base(storage, log, config)
    {

    }

    public IAsyncEnumerable<Scaleset> Search()
    {
        return QueryAsync();
    }

}
