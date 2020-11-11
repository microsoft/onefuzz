# What is this for?

This section of code contains scripts which help to deploy latest releases of OneFuzz at demand. It uses Azure DevOps Build Pipeline.

The script [deploy-onefuzz.yml](deploy-onefuzz.yml) can be used saved in Azure DevOps Build Pipeline or can be stored in the repository and can be pointed to it.

It also contain supporting `python` scripts which helps to fetch latest version and artifacts from OneFuzz GitHub repository.

# How to use it?

This script is intended only for deploying newer updates. There are certain set of pipeline variables needs to be set as mentioned in [deploy-onefuzz.yml](deploy-onefuzz.yml) for authentication purposes to the OneFuzz instance.