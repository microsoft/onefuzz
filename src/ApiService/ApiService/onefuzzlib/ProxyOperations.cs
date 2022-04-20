using ApiService.OneFuzzLib.Orm;
using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service;

public interface IProxyOperations : IOrm<Proxy>
{
    Task<Proxy?> GetByProxyId(Guid proxyId);
}
public class ProxyOperations : Orm<Proxy>, IProxyOperations
{
    private readonly ILogTracer _log;

    public ProxyOperations(ILogTracer log, IStorage storage)
        : base(storage)
    {
        _log = log;
    }

    public async Task<Proxy?> GetByProxyId(Guid proxyId)
    {

        var data = QueryAsync(filter: $"RowKey eq '{proxyId}'");

        return await data.FirstOrDefaultAsync();
    }
}
