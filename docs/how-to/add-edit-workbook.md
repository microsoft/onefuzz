# Add or Edit an Azure Monitor Workbook

## About

Azure Monitor Workbooks are a way to provide query-driven, lightweight reporting
from within the Azure Portal. You can read more about them
[here][workbooks-overview].

Workbooks can be deployed via ARM templates, and OneFuzz ships some out of the
box.

[workbooks-overview]:
  https://docs.microsoft.com/en-us/azure/azure-monitor/platform/workbooks-overview

## Steps

1. Create or edit a workbook in the Azure Portal

Create a new workbook instance, or open an existing instance in the Azure
Portal. Add parameters, queries, charts, and other elements, as desired.

2. Extract the `serializedData` that describes the workbook

While viewing an open workbook instance:

> 1.  Cick the "Edit" button.
>
> 2.  Click the Advanced Editor button (uses the `</>` "code" icon)
>
> 3.  Click the "ARM Template" tab.
>
> 4.  In the displayed JSON, copy the string value of the
>     `resources.properties.serializedData` field. Be sure to include the outer
>     double quotes, so the copied value is a serialized JSON object.

3. Update `workbook-data.json`

Each workbook is stored as a serialized JSON string value in
`deployments/workbook-data.json`.

The serialized workbook data will be referenced in `azuredeploy.json` or `azuredeploy.bicep` using the
property in `workbook-data.json`.

The value must be the exact string you copied from the example ARM Template in
the Advanced Editor view.

If adding a new workbook, add a new property and value. If editing a workbook,
overwrite the existing value.

4. Ensure the resource is deployed in `azuredeploy.json` or `azuredeploy.bicep`

To actually deploy a workbook instance, you must include it as a resource in
`azuredeploy.json` or `azuredeploy.bicep`.

It should be a child resource of the Log Analytics workspace resource
(`Microsoft.Insights/components` component).

Example:

```json
{
  "name": "<uuid>",
  "type": "microsoft.insights/workbooks",
  "location": "[resourceGroup().location]",
  "apiVersion": "2018-06-17-preview",
  "dependsOn": [
    "[resourceId('Microsoft.Insights/components', parameters('name'))]"
  ],
  "kind": "shared",
  "properties": {
    "displayName": "<display-name>",
    "serializedData": "[parameters('workbookData').<workbook-property>]",
    "version": "1.0",
    "sourceId": "[resourceId('Microsoft.Insights/components', parameters('name'))]",
    "category": "tsg"
  }
}
```

In the above, `<uuid>` is any unique UUID of your choosing. The `<display-name>`
value is the workbook display name, and `<workbook-property>` should be the
property name in `workbook-data.json` that maps to the `serializedData` for your
workbook.
