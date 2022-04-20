using ApiService.OneFuzzLib.Orm;
using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service;

public interface INodeOperations : IOrm<Node>
{
    Task<Node?> GetByMachineId(Guid machineId);
}

public class NodeOperations : Orm<Node>, INodeOperations
{

    public NodeOperations(IStorage storage)
        : base(storage)
    {

    }

    public async Task<Node?> GetByMachineId(Guid machineId)
    {
        var data = QueryAsync(filter: $"RowKey eq '{machineId}'");

        return await data.FirstOrDefaultAsync();
    }

}
