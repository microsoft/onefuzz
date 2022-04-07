using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service;

public partial record Proxy
{
    public async static Task<Proxy?> GetByProxyId(IStorageProvider storageProvider, Guid proxyId) {
        var tableClient  = await storageProvider.GetTableClient("Proxy");

        var data = storageProvider.QueryAsync<Proxy>(filter: $"RowKey eq '{proxyId}'");

        return await data.FirstOrDefaultAsync();
    }
}
