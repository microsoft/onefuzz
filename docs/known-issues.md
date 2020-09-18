# Known Issues

## Initial Deployments

1. A website with `myname` already exists

   This means someone already is using `myname.azurewebsites.net`. You'll need
   to pick a different name for your onefuzz instance.

1. The workspace name 'logs-wb-XXX' is not unique

   This means the workbook created by the onefuzz deployment is already
   allocated in a different resource group, even if said resource group has
   been deleted.

   1. Use a new resource group name
   1. Delete the referenced workbook manually following [Migrating Regions](migrating-regions.md)
   1. Wait a few weeks for Azure to automatically delete the deleted workbook.

1. PrincipalNotFound: Principal XXX does not exist in the directory YYY

   This means you encountered a race condition from the System allocated
   service principal for the function app deployment. You should be able
   to rerun the deploy script without issue.

1. Azure.Functions.Cli.Arm.ArmResourceNotFoundException: Can't find app with
   name "XXX"

   The resources for the onefuzz instance were deployed, but the SCM component
   of Azure Functions was not available yet. This race condition is solved by
   ARM reporting the deployment is finished too early. Retry the deployment and
   the error should be corrected automatically.

   Work item: #122629
