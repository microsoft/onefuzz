using System.Threading.Tasks;
using Azure.ResourceManager.Compute;

namespace Microsoft.OneFuzz.Service;

public interface IDiskOperations {
    DiskCollection ListDisks(string resourceGroup);

    Async.Task<bool> DeleteDisk(string resourceGroup, string name);
}

public class DiskOperations : IDiskOperations {
    private ILogTracer _logTracer;

    private ICreds _creds;

    public DiskOperations(ILogTracer log, ICreds creds) {
        _logTracer = log;
        _creds = creds;
    }

    public Task<bool> DeleteDisk(string resourceGroup, string name) {
        throw new NotImplementedException();
    }

    public DiskCollection ListDisks(string resourceGroup) {
        _logTracer.Info($"listing disks {resourceGroup}");
        return _creds.GetResourceGroupResource().GetDisks();
    }
}
