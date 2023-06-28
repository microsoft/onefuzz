# Troubleshooting issues with binaries

The agent exposes some validation tools to help troubleshoot issues with a binary.
This allows the user to debug and fix errors that could prevent a job from running.

## Using the validation tools
Download a zip file containing the agent binaries:

```
onefuzz tools get <destination_folder>
```

Extract the zip file in a folder of your choice.
Navigate to the folder that matches your os.
Run the following command to see the tools available:
`onefuzz-agent.exe validate --help`
The current list of commands are:
   - run_setup : Run the setup script
   - validate_libfuzzer:  Validate the libfuzzer target
   - execution_log: Get the execution logs to debug loading issues

   More tools might be added in the future so please refer the help command to get the most up to date list.


## In a docker container

It could also be helpful to run the those command in an environment to closely match the vm where the agent is deployed.
A docker container can help with that scenario.

Make sure [docker](https://docs.docker.com/desktop/) is installed and runs properly.

Navigate to the folder that matches your os in the tools folder created earlier and build the docker container:

```cmd
docker build --t <container_name> .
```

Use the container interactively to execute the validation command:

windows

```
docker run --it --rm --entrypoint powershell <image_name>
```

linux

```
docker run --it --rm --entrypoint bash <image_name>
```

From there you can navigate to the onefuzz directory and execute the validation commands

