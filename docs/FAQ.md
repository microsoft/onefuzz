# Frequently Asked Questions

## Results sometimes show up before tasks are "running"

We use VM Scale Sets. Often, some of the VMs in the set provision faster than
others. Rather than wait for the entire set to begin, the agent starts on each
VM as soon as the VM is up.

## Debugging issues on scalesets

You can use az vmss run-command to launch commands in your VMs. As an example,
the following command in bash will recursively list c:\onefuzz for a given task:

```sh
az vmss list-instances --subscription SUBSCRIPTION -n TASK_ID -g RESOURCE_GROUP \
 --query [].id --output tsv | az vmss run-command invoke --ids @- \
 --command-id RunPowerShellScript --scripts 'Get-ChildItem -Path c:\onefuzz -Recurse'
```

On Linux VMs, use RunShellScript. On Windows VMs, use RunPowerShellScript. Note
that you will only see the last 4096 bytes of output. See
[here](https://docs.microsoft.com/en-us/azure/virtual-machines/linux/run-command#restrictions)
for all restrictions on run-command.
