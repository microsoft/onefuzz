using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service;

public partial record Node
{
    public async static Task<Node?> GetByMachineId(IStorageProvider storageProvider, Guid machineId) {
        var tableClient  = await storageProvider.GetTableClient("Node");

        var data = storageProvider.QueryAsync<Node>(filter: $"RowKey eq '{machineId}'");

        return await data.FirstOrDefaultAsync();
    }
}
