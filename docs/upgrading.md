# Upgrading OneFuzz Instances

Upgrading is accomplished by deploying OneFuzz to the same resource group and
instance name.

Unless the release includes breaking changes, as indicated by the
[versioning.md](versioning guidelines), currently running jobs should not be
negatively impacted during the upgrade process.

Data from on-going jobs is transmitted through Storage Queues and Storage
Containers, which will buffer data during the upgrade process.

Users should take care over the following items:

1. Any customization to the Azure Functions instance will likely get
   overwritten.
1. The following containers will be synchronized (and remove out-dated content)
   on upgrade.
   1. The `tools` container in the `func` storage account
   1. The third-party tools containers in the `fuzz` storage account. At the
      time of writing, these include:
      * radamsa-linux
      * radamsa-windows
      * afl-linux
      * aflpp-linux
1. Any jobs deployed during the upgrade process may temporarily fail to be
   submitted.
   The CLI will automatic retry to submit jobs that fail due error codes known
   to happen during the service upgrade procedure. If this behavior is
   undesired, please pause submission of jobs during the upgrade process.
