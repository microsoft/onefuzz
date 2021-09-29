# Deploying OneFuzz jobs via GitHub Actions

This pipeline uses an [AAD Service Principal](https://docs.microsoft.com/en-us/azure/active-directory/develop/app-objects-and-service-principals) to authenticate to Onefuzz.

To create work items upon finding crashes, this pipeline uses a [GitHub Personal Access Token](https://github.com/settings/tokens) to report any crashes found during fuzzing as [GitHub Issues](../../docs/notifications/github.md).

## Configuration
This example uses [Encrypted Secrets](https://docs.github.com/en/actions/reference/encrypted-secrets) to configure the workflow:
* `onefuzz_endpoint`: The Onefuzz Instance.  This should be the URL for the instance, such as `https://onefuzz-playground.azurewebsites.net`.
* `onefuzz_client_id`: The Client ID for the [service principal](https://docs.microsoft.com/en-us/azure/active-directory/develop/app-objects-and-service-principals).
* `onefuzz_client_secret`: The Client Secret for the [service principal](https://docs.microsoft.com/en-us/azure/active-directory/develop/app-objects-and-service-principals).
* `onefuzz_pat`: The [GitHub Personal Access Token](https://github.com/settings/tokens).

This example uses environment variables to configure the workflow:
* `ONEFUZZ_PROJECT`:The name of your project.  As an example, "Browser".
* `ONEFUZZ_NAME`: The name of your target application.  As an example, "jpg-parser".
* `ONEFUZZ_POOL`:The name of the fuzzing [Pool](../../docs/terminology.md#pool) to use.  As an example, `linux`.

### GitHub Issues Configuration
In the [notification configuration](github-issues.json), there are a few items that are hard-coded that you should update for your instance:
* Replace `INSERT_YOUR_USERNAME_HERE` with the name of your GitHub username used to file issues.
* Replace `organization` with the name of your GitHub organization to file issues.
* Replace `repository` with the name of your GitHub repository to file issues.
