using ApiService.OneFuzzLib.Orm;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service;

public interface IProxyOperations : IOrm<Proxy>
{
    Task<Proxy?> GetByProxyId(Guid proxyId);
}
public class ProxyOperations : Orm<Proxy>, IProxyOperations
{
    private readonly ILogTracerFactory _logger;

    public ProxyOperations(ILogTracerFactory loggerFactory, IStorage storage)
        : base(storage)
    {
        _logger = loggerFactory;
    }

    public async Task<Proxy?> GetByProxyId(Guid proxyId)
    {

        var data = QueryAsync(filter: $"RowKey eq '{proxyId}'");

        return await data.FirstOrDefaultAsync();
    }
}
