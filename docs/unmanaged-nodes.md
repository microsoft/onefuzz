# Unmanaged Nodes

The default mode of OneFuzz is to run the agents inside scalesets managed by the the Onefuzz instance. But it is possible to run outside of the Instance infrastructure.
This is the unmanaged scenario. In this mode, the user can use their own resource to participate in the fuzzing.

## Set-up

These are the steps to run an unmanaged node.

### Create an Application Registration in Azure Active Directory

Create the authentication method for the unmanaged node.
From the [azure cli](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) create a new **application registration**:

```cmd
az ad app create --display-name <registration_name>
```

Then use the application's `app_id` in the newly created application registration to create the associated **service principal**:

```cmd
az ad sp create --id <app_id>
```

Take note of the `id` returned by this request. We will call it the `principal_id`.

Next, create a `client_secret`:

```
az ad app credential reset --id <app_id> --append
```

Take note of the `password` returned.

### Authorize the application in OneFuzz

From the OneFuzz `deployment` folder run the following script using the `app_id` from above:

```cmd
python .\deploylib\registration.py register_app <onefuzz_instance_id> <subscription_id> --app_id <app_id> --role UnmanagedNode
```

### Create an unmanaged pool

Using the OneFuzz CLI:

```cmd
onefuzz pools create <pool_name> <os> --unmanaged --object_id <principal_id>
```

### Download the agent binaries and the agent configuration

Download a zip file containing the agent binaries:

```
onefuzz tools get <destination_folder>
```

Extract the zip file in a folder of your choice.

Download the configuration file for the agent:

```
onefuzz pools get_config <pool_name>
```

Under the `client_credential` section of the agent config file, update `client_id` and `client_secret`:

```json
{
  "client_id": "<app_id>",
  "client_secret": "<password>"
}
```

Save the config to the file.

### Start the agent

Navigate to the folder corresponding to your OS.
Set the necessary environment variable by running the script `set-env.ps1` (for Windows) or `set-env.sh` (for Linux).
Run the agent with the following command. If you need more nodes, use a different `machine_guid` for each one:

```cmd
onefuzz-agent run --machine_id <machine_guid> -c <path_to_config_file> --reset_lock
```

Alternatively, the agent folder contains a Dockerfile which provide the configuration of a docker container.
you can use it by first building the container

```cmd
docker build --t <container_name> .
```

Then start the agent inside the container

```cmd
docker run  <container_name> --machine_id <machine_id> --reset_lock
```

### Verify that the agent is registered to OneFuzz

Using the OneFuzz CLI run the following command:

```
onefuzz nodes get <machine_guid>
```

This should return one entry. Verify that the `pool_name` matched the pool name created earlier.
From here you will be able to schedule jobs on that pool and they will run.

## Troubleshooting

### Increase the verbosity of the logs

It can help when investigating issues to increase the log verbosity. you will need to set the [RUST_LOG](https://docs.rs/env_logger/latest/env_logger/#enabling-logging) environment variable when starting docker

```
docker run --rm --env RUST_LOG=<log_level> <image_name> --machine_id <machine_id>
```

log_level can be any of

- error
- warn
- info
- debug
- trace

### Use the container interactively

you can use the container interactively by with the following command

windows

```
docker run --it --rm --entrypoint powershell <image_name>
```

linux

```
docker run --it --rm --entrypoint bash <image_name>
```

### Mount a local folder in the container

docker allows you to [mount](https://docs.docker.com/storage/bind-mounts/#mount-into-a-non-empty-directory-on-the-container) a local folder when running a container

```
docker run -it --rm --mount type=bind,source=<local_path>,target=<path_in_container>
```