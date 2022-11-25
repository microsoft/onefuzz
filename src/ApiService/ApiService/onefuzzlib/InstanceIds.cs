namespace Microsoft.OneFuzz.Service;

public static class InstanceIds {
    // See: https://learn.microsoft.com/en-us/azure/virtual-machine-scale-sets/virtual-machine-scale-sets-instance-ids#scale-set-vm-names
    // Machine Name here is {ScaleSet}_{InstanceId}
    public static string InstanceIdFromMachineName(string machineName)
        => machineName.Split("_").Last();
}
