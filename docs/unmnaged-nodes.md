# Unmanaged Nodes

The default mode of onefuzz is to run agent inside scaleset managed byt he the Onefuzz instance. But it is possible to run outside of the Instance infrastrutire.
This is called the unmanged scenario.
IN this mode the user can use its own resource to participate in the fuzzing.


## Set-up

These are the steps to run an unmanaged node

### 1- Create an application registration in azure active directory

We will create the authentication method for the unmanaged node.

From the [azure cli](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) create a new **application registration**
```cmd
az ad app create --display-name chkeita_test_unmanaged_2
```
Then use the application `app_id` in the result to create the associated **service principal**

```cmd
az ad sp create --id <app_id>
```
take note of the `id` returned by this request. We will call it the `principal_id`

create an client_secret

```
az ad app credential reset --id da0759c4-3772-4a78-9b06-551b4538e3db --append
```

take note of the `password` returned.

### 2- Authorize the application in onefuzz


From onefuzz deployment folder run the following script using the app_id
``` cmd
python .\deploylib\registration.py register_app <onefuzz_instance_id> <subscription_id> --app_id <app_id> --role UnmanagedNode
```

### 3- Create an unmanged pool

from the onefuzz cli
``` cmd
onefuzz pools create <pool_name> <os> --unmanaged --object_id <principal_id>
```


### 4- Download the agent binaries and the agent configuration

download a zip file containing the agent binaries
```
onefuzz tools get <destination_folder>
```

download the configuration file for the agent

```
onefuzz pools get_config <pool_name>
```

under the client_credential section of the agent config file. update client_id and client
```json
{
    "client_id": "<app_id>",
    "client_secret": "<password>",
}
```
save the config to file.

### 5- start the agent.

After extracting the agent in the folder of your choice. navigate to folder corresponding to your os.

Set the necessary environment variable by running the script set-env.ps1 of set-env.sh depending on your platform

Run the agent wit the following command. If you need more nodes use different `machine_guid` for each
```cmd
onefuzz-agent run --machine_id "<machine_guid>" -c <path_to_config_file>
```

### 6- verify that the agent is registered to OneFuzz

in the onefuzz cli run the following command

```
onefuzz nodes get b5960858-a054-47be-8a1f-6558e555bbf3
```

this should return one entry. Verify that the `pool_name` matched the pool name created earlier

From here you will be able to schedule jobs on that pool and they will be runnin