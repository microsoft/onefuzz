# Getting started using OneFuzz

If you have access to an existing OneFuzz instance, skip ahead to [Deploying Jobs](#deploying-jobs).

**Microsoft employees:** Please join the [Fuzzing @ Microsoft](https://aka.ms/fuzzingatmicrosoft) team for support.

## Choosing a subscription

An instance of OneFuzz is a collection of Azure resources contained within a single Azure resource group.
If it doesn't already exist, this resource group will be created for you when running the deploy script.
However, the subscription itself must exist and have the following Azure
[Resource Providers](https://docs.microsoft.com/en-us/azure/azure-resource-manager/management/resource-providers-and-types#register-resource-provider)
registered:
- `Microsoft.EventGrid`
- `Microsoft.Network`
- `Microsoft.Compute`
- `Microsoft.SignalRService`

## Deploying an instance of OneFuzz

Ensure you have Python with `python --version` >= 3.7, [Azure Functions Core Tools
v3](https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local),
and OpenSSL installed.

From the [Latest Release of
OneFuzz](https://github.com/microsoft/onefuzz/releases) download the
`onefuzz-deployment` package.

On a host with the [Azure CLI logged
in](https://docs.microsoft.com/en-us/cli/azure/authenticate-azure-cli?view=azure-cli-latest),
do the following:

```
unzip onefuzz-deployment-$VERSION.zip
pip install -r requirements.txt
./deploy.py $REGION $RESOURCE_GROUP_NAME $ONEFUZZ_INSTANCE_NAME $CONTACT_EMAIL_ADDRESS $NSG_CONFIG_FILE
```

When running `deploy.py` the first time for an instance, you will be prompted
to follow a manual step to initialize your CLI config.

The $NSG_CONFIG_FILE is a required parameter that specifies the 'allow rules' for the OneFuzz Network Security Group. A default `config.json` is provided in the deployment zip. 
This 'allow' config resembles the following: 
```
{
    "proxy_nsg_config": {
        "allowed_ips": ["*"],
        "allowed_service_tags": []
    }
}
```
Future updates can be made to this configuration via the OneFuzz CLI. 

## Install the CLI

Download the Python SDK (make sure to download both `onefuzz` and `onefuzztypes`)
from the [Latest Release of OneFuzz](https://github.com/microsoft/onefuzz/releases).

If you're using the SDK, install via:

```
pip install ./onefuzz*.whl
```

### Connecting to your instance

Use the `onefuzz config` command to specify your instance of OneFuzz.

```
$ onefuzz config --endpoint https://$ONEFUZZ_INSTANCE_NAME.azurewebsites.net
$ onefuzz versions check --exact
"compatible"
$
```

From here, you can use OneFuzz.

## Creating Worker Pools

OneFuzz distributes tasks to pools of workers, and manages workers using [VM Scalesets](https://azure.microsoft.com/en-us/services/virtual-machine-scale-sets/).

To create a pool:

```
$ onefuzz pools create my-pool linux --query pool_id
"9e779388-a9c2-4934-9fa2-6ed6f6a7792a"
$
```

To create a managed scaleset of Ubuntu 18.04 VMs using a [general purpose
Azure VM](https://docs.microsoft.com/en-us/azure/virtual-machines/sizes) that
belongs to the pool:

```
$ onefuzz scalesets create my-pool $VM_COUNT
{
    "image": "Canonical:UbuntuServer:18.04-LTS:latest",
    "pool_name": "my-pool",
    "region": "eastus",
    "scaleset_id": "eb1e9602-4acf-40b8-9216-a5d598d27195",
    "size": 3,
    "spot_instances": false,
    "state": "init",
    "tags": {},
    "vm_sku": "Standard_DS1_v2"
}
$
```

## Deploying Jobs

Users can deploy fuzzing jobs using customized fuzzing pipelines or using
pre-built templates.

For most use cases, pre-built templates are the best choice.

### Building a Libfuzzer Target

Building your first target to run in OneFuzz:

```
$ git clone -q https://github.com/microsoft/onefuzz-samples
$ cd onefuzz-samples/examples/simple-libfuzzer
$ make
clang -g3 -fsanitize=fuzzer -fsanitize=address fuzz.c -o fuzz.exe
$
```

### Launching a Job

With a built fuzzing target, launching a libFuzzer-based job can be done in
a single command:

```
$ onefuzz template libfuzzer basic my-project my-target build-1 my-pool --target_exe fuzz.exe
INFO:onefuzz:creating libfuzzer from template
INFO:onefuzz:creating job (runtime: 24 hours)
INFO:onefuzz:created job: a6eda06f-d2e3-4a50-8754-1c1de5c6ea23
INFO:onefuzz:using container: oft-setup-f83f5d9b34305bf98ee56a5944fb5fa3
INFO:onefuzz:using container: oft-inputs-14b8ea05ca635426bd9ccf3ee71b2e45
INFO:onefuzz:using container: oft-crashes-14b8ea05ca635426bd9ccf3ee71b2e45
INFO:onefuzz:using container: oft-reports-14b8ea05ca635426bd9ccf3ee71b2e45
INFO:onefuzz:using container: oft-unique-reports-14b8ea05ca635426bd9ccf3ee71b2e45
INFO:onefuzz:using container: oft-no-repro-14b8ea05ca635426bd9ccf3ee71b2e45
INFO:onefuzz:using container: oft-coverage-14b8ea05ca635426bd9ccf3ee71b2e45
INFO:onefuzz:uploading target exe `fuzz.exe`
INFO:onefuzz:creating libfuzzer task
INFO:onefuzz:creating libfuzzer_coverage task
INFO:onefuzz:creating libfuzzer_crash_report task
INFO:onefuzz:done creating tasks
$
```

### Launching a job from the SDK

Every action from the CLI is exposed in the SDK.  Launching the same template as above
can be done with the Python SDK:

```
$ python
>>> from onefuzz.api import Onefuzz
>>> Onefuzz().template.libfuzzer.basic('my-project', 'my-first-job', 'build-1', 'my-pool', target_exe="fuzz.exe")
>>>
```

## Investigating Crashes

Enabling [notifications](notifications.md) provides automatic reporting of identified
crashes.  The CLI can be used to manually monitor for crash reports:

```
$ onefuzz jobs containers list a6eda06f-d2e3-4a50-8754-1c1de5c6ea23 --container_type unique_reports
{
"oft-unique-reports-05ca06fd172b5db6a862a38e95c83730": [
        "972a371a291ed5668a77576368ead0c46c2bac9f9a16b7fa7c0b48aec5b059b1.json"
    ]
}
$
```

Then view the results of a crash report with [jq](https://github.com/stedolan/jq):
```
$ onefuzz containers files get oft-unique-reports-05ca06fd172b5db6a862a38e95c83730 972a371a291ed5668a77576368ead0c46c2bac9f9a16b7fa7c0b48aec5b059b1.json > report.json
$ jq .call_stack report.json
[
  "#0 0x4fd706 in LLVMFuzzerTestOneInput /home/vsts/work/1/s/sample-target.c:50:83",
  "#1 0x43b271 in fuzzer::Fuzzer::ExecuteCallback(unsigned char const*, unsigned long) (/onefuzz/setup/fuzz.exe+0x43b271)",
  "#2 0x423767 in fuzzer::RunOneTest(fuzzer::Fuzzer*, char const*, unsigned long) (/onefuzz/setup/fuzz.exe+0x423767)",
  "#3 0x429741 in fuzzer::FuzzerDriver(int*, char***, int (*)(unsigned char const*, unsigned long)) (/onefuzz/setup/fuzz.exe+0x429741)",
  "#4 0x4557a2 in main (/onefuzz/setup/fuzz.exe+0x4557a2)",
  "#5 0x7ffff6a9bb96 in __libc_start_main /build/glibc-2ORdQG/glibc-2.27/csu/../csu/libc-start.c:310",
  "#6 0x41db59 in _start (/onefuzz/setup/fuzz.exe+0x41db59)"
]
$
```

### Live debugging of a crash sample

Using the crash report, OneFuzz can enable live remote debugging of the crash
using a platform-appropriate debugger (gdb for Linux and cdb for Windows):

```
$ onefuzz repro create_and_connect get oft-unique-reports-05ca06fd172b5db6a862a38e95c83730 972a371a291ed5668a77576368ead0c46c2bac9f9a16b7fa7c0b48aec5b059b1.json
INFO:onefuzz:creating repro vm: get oft-unique-reports-05ca06fd172b5db6a862a38e95c83730 972a371a291ed5668a77576368ead0c46c2bac9f9a16b7fa7c0b48aec5b059b1.json (24 hours)
INFO:onefuzz:connecting to reproduction VM: c6525b82-7269-45ee-8a62-2d9d61d1e269
- launching reproducing vm.  current state: extensions_launch
Remote debugging using :1337
Reading /onefuzz/setup/fuzz.exe from remote target...
warning: File transfers from remote targets can be slow. Use "set sysroot" to access files locally instead.
Reading /onefuzz/setup/fuzz.exe from remote target...
Reading symbols from target:/onefuzz/setup/fuzz.exe...done.
Reading /lib64/ld-linux-x86-64.so.2 from remote target...
Reading /lib64/ld-linux-x86-64.so.2 from remote target...
Reading symbols from target:/lib64/ld-linux-x86-64.so.2...Reading /lib64/ld-2.27.so from remote target...
Reading /lib64/.debug/ld-2.27.so from remote target...
(no debugging symbols found)...done.
0x00007ffff7dd6090 in ?? () from target:/lib64/ld-linux-x86-64.so.2
(gdb) info reg rip
rip            0x7ffff7dd6090      0x7ffff7dd6090
(gdb)
```
