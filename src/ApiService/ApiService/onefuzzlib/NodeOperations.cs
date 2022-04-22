using ApiService.OneFuzzLib.Orm;
using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service;

public interface INodeOperations : IStatefulOrm<Node, NodeState>
{
    Task<Node?> GetByMachineId(Guid machineId);
}

public class NodeOperations : StatefulOrm<Node, NodeState>, INodeOperations
{

    public NodeOperations(IStorage storage, ILogTracer log)
        : base(storage, log)
    {

    }

    public async Task<Node?> GetByMachineId(Guid machineId)
    {
        var data = QueryAsync(filter: $"RowKey eq '{machineId}'");

        return await data.FirstOrDefaultAsync();
    }

}
