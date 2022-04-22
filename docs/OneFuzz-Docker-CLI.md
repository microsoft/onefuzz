# OneFuzz CLI in a Docker container

## Using official release Docker container
TODO

## Building your own Docker container

Docker file is located in `src` folder.

To buid your own OneFuzz CLI Docker container use following command from `src` folder
```
docker build . --tag <CONTAINER_TAG> --build-arg REPO=<GITHUB_REPO> --build-arg PR=<PR> --build-arg GITHUB_TOKEN=<GITHUB_TOKEN>
```
where

- <CONTAINER_TAG> - container image tag, it's an optional parameter. It will be used later in the document to explain how to run the container.

- <GITHUB_REPO> - GitHub repository that contains a successfully build pull request that will be used for creating Docker container.
- <PR> - GitHub pull request number that contains build artifacts to use to create Docker container.
- <GITHUB_TOKEN> - In GitHub, generate a personal access token (PAT) with the `public_repo` scope.
   You may need to enable SSO for the token, depending on the org that your OneFuzz fork belongs to.


## Running OneFuzz CLI Docker container

There are three different scenarios that get enabled with OneFuzz CLI Docker container

1. To have a new OneFuzz CLI session where you need to configure and authenticate every time on Docker container startup use following command `docker run -it <CONTAINER_TAG>`.

2. If you have used OneFuzz CLI in your dev environment, and want to re-use configuration and authentication cache. Run following command (PowerShell example)  `docker run -it -v $env:USERPROFILE\.cache\onefuzz:/root/.cache/onefuzz <CONTAINER_TAG>`. It will mount your OneFuzz cache folder into OneFuzz CLI Docker container.

3. If you have several OneFuzz deployments. You can store OneFuzz configuration per deployment in your dev environment by creating a different folder for each OneFuzz deployment and then mounting that folder as OneFuzz CLI cache when running the Docker container.
`docker run -it -v <ONEFUZZ_CONFIG_FOLDER>:/root/.cache/onefuzz <CONTAINER_TAG>`