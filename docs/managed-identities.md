# Managed Identities in OneFuzz

OneFuzz makes use of
[Managed identities](https://docs.microsoft.com/en-us/azure/active-directory/managed-identities-azure-resources/overview)
both in the API service as well as the managed VMs.

There are currently two uses of Managed Identities within OneFuzz:

1. The API service manages the full lifecycle of VMs, VM Scalesets, and Networks
   in use in OneFuzz. In order to enable this, the service must have appropriate
   role assignments permissions to manage these resources. At the moment, the
   role assignments granted to the OneFuzz API are:

   1. [Virtual Machine Contributor](https://docs.microsoft.com/en-us/azure/role-based-access-control/built-in-roles#virtual-machine-contributor)
   1. [Network Contributor](https://docs.microsoft.com/en-us/azure/role-based-access-control/built-in-roles#network-contributor)
   1. [Log Analytics Contributor](https://docs.microsoft.com/en-us/azure/role-based-access-control/built-in-roles#log-analytics-contributor)

   See [azuredeploy.json](../src/deployment/azuredeploy.json) for the specific
   implementation of these role assignments.

   or

   See [azuredeploy.bicep](../src/deployment/azuredeploy.bicep) for the specific
   implementation of these role assignments.

1. VMs created by OneFuzz are created using the Managed Identities without roles
   assigned in order to enable the OneFuzz agent running in the VMs to
   authenticate to the service itself.
