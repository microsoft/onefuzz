using ApiService.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IScalesetOperations : IOrm<Scaleset>
{
    IAsyncEnumerable<Scaleset> Search();

    public IAsyncEnumerable<Scaleset?> SearchByPool(string poolName);

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

    public IAsyncEnumerable<Scaleset?> SearchByPool(string poolName) {
        return QueryAsync(filter: $"pool_name eq '{poolName}'");
    }

}
