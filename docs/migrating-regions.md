# Instructions

To migrate an instance to a new region, do the following:

1. Manually hard-delete the Azure Monitor workbook / Log Analytics Workspace.
   (See instructions below)
1. Delete resource group. (Example: `az group delete -y GROUP_NAME`)
1. Delete
   [RBAC entry](https://ms.portal.azure.com/#blade/Microsoft_AAD_IAM/ActiveDirectoryMenuBlade/RegisteredApps)
1. Deploy instance using the new region name

If you try deleting the resource group and moving to a different region without
the above procedure, you'll get an error like this:

`"The workspace name 'logs-wb-XXXX' is not unique"`

## Hard-deleting a Log Analytics Workspace

Full, official instructions for deleting a Log Analytics Workspace can be found
[here](https://docs.microsoft.com/en-us/azure/azure-monitor/platform/delete-workspace#permanent-workspace-delete).
Review this page before continuing.

To summarize, you need your subscription ID, resource group name, log analytics
workspace name, and a valid bearer token, to authenticate a REST API (only!)
DELETE request.

To quickly, manually get a bearer token, you can go to any Azure REST API
documentation page with a green "Try It" button. For example,
[this one](https://docs.microsoft.com/en-us/rest/api/compute/virtualmachines/list).

Click "Try It", authenticate, and you will find a valid `Authorization` header
in the "Preview" section at the bottom of the "REST API Try It" pane. Copy the
_entire_ header value (i.e. `Authorization: Bearer <base64-bearer-token-data>`).
Remember, this is a credential (and will expire), so do not log it, track it in
version control, &c.

You can then edit the following bash script template (or equivalent) to
permanently delete the workspace:

```bash
#!/usr/bin/env bash

# Set to the value copy-pasted from the Try It pane, like:
# "Authorization: Bearer ${BEARER}"
AUTH_H='<copied-authorization-header>'

# Set these variables using values from your resource group.
SUBSCRIPTION='<subscription-id>'
RESOURCE_GROUP='<resource-group-name>'
WORKSPACE_NAME='<log-analytics-workspace-name>'

# Does not need to be edited.
URL="https://management.azure.com/subscriptions/${SUBSCRIPTION}/resourcegroups/${RESOURCE_GROUP}/providers/Microsoft.OperationalInsights/workspaces/${WORKSPACE_NAME}?api-version=2015-11-01-preview&force=true"

# Requires the cURL command.
curl -X DELETE -H "${AUTH_H}" "${URL}"
```
