using System.Threading.Tasks;
using Azure;
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

    public async Task<bool> DeleteDisk(string resourceGroup, string name) {
        try {
            _logTracer.Info($"deleting disks {resourceGroup:Tag:ResourceGroup} - {name:Tag:Name}");
            var disk = await _creds.GetResourceGroupResource().GetDiskAsync(name);
            if (disk != null) {
                _ = await disk.Value.DeleteAsync(WaitUntil.Started);
                return true;
            }
        } catch (Exception e) {
            _logTracer.Error($"unable to delete disk: {name:Tag:Name} {e.Message:Tag:Error}");
            _logTracer.Exception(e);
        }
        return false;
    }

    public DiskCollection ListDisks(string resourceGroup) {
        _logTracer.Info($"listing disks {resourceGroup:Tag:ResourceGroup}");
        return _creds.GetResourceGroupResource().GetDisks();
    }
}
