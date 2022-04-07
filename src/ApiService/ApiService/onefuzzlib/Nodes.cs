using ApiService.OneFuzzLib.Orm;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service;

public class Nodes
{
    public readonly IOrm _orm;
    public readonly IStorage _storage;
    public Nodes(IOrm orm, IStorage storage)
    {
        _orm = orm;
        _storage = storage;
    }

    public async Task<Node?> GetByMachineId(IStorageProvider storageProvider, Guid machineId) {
        var tableClient  = await _orm.GetTableClient("Node");

        var data = _storage.QueryAsync<Node>(filter: $"RowKey eq '{machineId}'");

        return await data.FirstOrDefaultAsync();
    }
}
