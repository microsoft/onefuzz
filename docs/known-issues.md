# Known Issues

## Initial Deployments

1. A website with `myname` already exists

   This means someone already is using `myname.azurewebsites.net`. You'll need
   to pick a different name for your onefuzz instance.

2. The workspace name 'logs-wb-XXX' is not unique

   This means the workbook created by the onefuzz deployment is already
   allocated in a different resource group, even if said resource group has
   been deleted.

   1. Use a new resource group name
   2. Delete the referenced workbook manually following [Migrating Regions](migrating-regions.md)
   3. Wait a few weeks for Azure to automatically delete the deleted workbook.

3. PrincipalNotFound: Principal XXX does not exist in the directory YYY

   This means you encountered a race condition from the System allocated
   service principal for the function app deployment. You should be able
   to rerun the deploy script without issue.

4. Azure.Functions.Cli.Arm.ArmResourceNotFoundException: Can't find app with
   name "XXX"

   The resources for the onefuzz instance were deployed, but the SCM component
   of Azure Functions was not available yet. This race condition is solved by
   ARM reporting the deployment is finished too early. Retry the deployment and
   the error should be corrected automatically.

5. Registration.GraphQueryError: request did not succeed: HTTP 403
   ```
   error: {
      ...
      "code": "Authorization_RequestDenied",
      "message": "Insufficient privileges to complete the operation.",
      ...}
   ```

   The application registration was created by different user and the current deployer does not have access to it.
   There are two ways to to solve the issue:

   1. Delete the application registration.
      In the Azure Portal, go to Azure Active Directory > App registrations > (search for your OneFuzz instance name) > Delete.

   2. Add the service principal currently deploying the application as an owner to the registration.
      Go to Azure Active Directory > App registrations > (search for your onefuzz instance name).
      In the Owner tab, add the service principal.
      In the Overview tab, click the link under "Managed application in local directory" > Owner, then add the service principal.

6. {'code': 'RoleAssignmentUpdateNotPermitted', 'message': 'Tenant ID, application ID, principal ID, and scope are not allowed to be updated.'}

   To fix the issue, remove all non existing service principals from the resource group.
   Go to the resource group > Access Control (IAM) > Role Assignments.
   Remove all entries marked as "Identity not found".