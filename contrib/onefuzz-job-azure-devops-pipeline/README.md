# Deploying OneFuzz jobs via ADO Pipelines

This pipeline uses an [AAD Service Principal](https://docs.microsoft.com/en-us/azure/active-directory/develop/app-objects-and-service-principals) to authenticate to Onefuzz.

To create work items upon finding crashes, this pipeline uses a [Azure Devops Personal Access Token](https://docs.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate) to report any crashes found during fuzzing as [Azure Devops Work Items](../../docs/notifications/ado.md).

## Configuration

This example uses the [Azure Devops Variable Group](https://docs.microsoft.com/en-us/azure/devops/pipelines/library/variable-groups), named `onefuzz-config`, which can be shared across multiple pipelines.   The following variables are defined in `onefuzz-config`:
* `endpoint`: The Onefuzz Instance.  This should be the URL of the instance, such as `https://onefuzz-playground.azurewebsites.net`.
* `client_id`: The Client ID of the [service principal]((https://docs.microsoft.com/en-us/azure/active-directory/develop/app-objects-and-service-principals).
* `client_secret`: The Client Secret of the [service principal](https://docs.microsoft.com/en-us/azure/active-directory/develop/app-objects-and-service-principals).
* `ado_pat`: The [Azure Devops Personal Access Token](https://docs.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate).  This should be a "secret" variable.

This example uses [Azure Devops Runtime parameters](https://docs.microsoft.com/en-us/azure/devops/pipelines/process/runtime-parameters), which are specific to this pipeline.  The following parameters are defined in this pipeline:
* `onefuzz_project`: The name of your project.  As an example, "Browser".  Unless otherwise specified, this defaults to `sample`.
* `onefuzz_target`: The name of your target.  As an example, "jpg-parser".  Unless otherwise specified, this defaults to `sample`.
* `onefuzz_pool`: The name of the fuzzing [Pool](../../docs/terminology.md#pool) to use.  Unless otherwise specified, this defaults to `linux`.

### Azure Devops Configuration
In the [notification configuration](ado-work-items.json), there are a few items that are hard-coded that you should update for your instance:
* Replace `INSERT_YOUR_ORG_HERE` with the name of your Azure Devops organization.
* Replace `INSERT_YOUR_PROJECT_HERE` with the name of your Azure Devops project.
* Replace `OneFuzz-Ado-Integration` with the Area Path for your work items to be filed.
