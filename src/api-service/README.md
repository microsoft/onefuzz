If you are doing development on the API service, you can build and deploy directly to your own instance using the azure-functions-core-tools.

From the api-service directory, do the following:

    func azure functionapp publish <instance>

While Azure Functions will restart your instance with the new code, it may take a while.  It may be helpful to restart your instance after pushing by doing the following:

    az functionapp restart -g <group> -n <instance>