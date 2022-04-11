using ApiService.OneFuzzLib.Orm;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.OneFuzz.Service;

public interface IProxyOperations : IOrm<Proxy>
{
    Task<Proxy?> GetByProxyId(Guid proxyId);
}
public class ProxyOperations : Orm<Proxy>, IProxyOperations
{
    private readonly ILogger _logger;

    public ProxyOperations(ILoggerFactory loggerFactory, IStorage storage)
        : base(storage)
    {
        _logger = loggerFactory.CreateLogger<QueueProxyHearbeat>();
    }

    public async Task<Proxy?> GetByProxyId(Guid proxyId)
    {

        var data = QueryAsync(filter: $"RowKey eq '{proxyId}'");

        return await data.FirstOrDefaultAsync();
    }
}
