using System.Threading.Tasks;
using Azure;
using Azure.ResourceManager.Compute;
using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;

public interface IDiskOperations {
    DiskCollection ListDisks(string resourceGroup);

    Async.Task<bool> DeleteDisk(string resourceGroup, string name);
}

public class DiskOperations : IDiskOperations {
    private ILogger _logTracer;

    private ICreds _creds;

    public DiskOperations(ILogger<DiskOperations> log, ICreds creds) {
        _logTracer = log;
        _creds = creds;
    }

    public async Task<bool> DeleteDisk(string resourceGroup, string name) {
        try {
            _logTracer.LogInformation("deleting disks {ResourceGroup} - {Name}", resourceGroup, name);
            var disk = await _creds.GetResourceGroupResource().GetDiskAsync(name);
            if (disk != null) {
                _ = await disk.Value.DeleteAsync(WaitUntil.Started);
                return true;
            }
        } catch (Exception e) {
            _logTracer.LogError("unable to delete disk: {Name} {Error}", name, e.Message);
            _logTracer.LogError(e, "DeleteDisk");
        }
        return false;
    }

    public DiskCollection ListDisks(string resourceGroup) {
        _logTracer.LogInformation("listing disks {ResourceGroup}", resourceGroup);
        return _creds.GetResourceGroupResource().GetDisks();
    }
}
