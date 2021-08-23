# OneFuzz

## A self-hosted Fuzzing-As-A-Service platform

Project OneFuzz enables continuous developer-driven fuzzing to proactively
harden software prior to release.  With a [single 
command](docs/getting-started.md#launching-a-job), which can be [baked into
CICD](contrib/onefuzz-job-github-actions/README.md), developers can launch
fuzz jobs from a few virtual machines to thousands of cores.

## Build Status

![Build Onefuzz](https://github.com/microsoft/onefuzz/workflows/Build/badge.svg?branch=main)

## Features

* **Composable fuzzing workflows**: Open source allows users to onboard their own 
   fuzzers, [swap instrumentation](docs/custom-analysis.md), and manage seed inputs.
* **Built-in ensemble fuzzing**: By default, fuzzers work as a team to share strengths, 
   swapping inputs of interest between fuzzing technologies.
* **Programmatic triage and result de-duplication**: It provides unique flaw cases that 
   always reproduce.
* **On-demand live-debugging of found crashes**: It lets you summon a live debugging
   session on-demand or from your build system.
* **Observable and Debug-able**: Transparent design allows introspection into every 
   stage.
* **Fuzz on Windows and Linux**: Multi-platform by design. Fuzz using your own [OS 
   build](docs/custom-images.md), kernel, or nested hypervisor.
* **Crash reporting notification callbacks**: Including [Azure DevOps Work
   Items](docs/notifications/ado.md) and [Microsoft Teams
   messages](docs/notifications/teams.md)

For information, check out some of our guides:
* [Terminology](docs/terminology.md)
* [Getting Started](docs/getting-started.md)
* [Supported Platforms](docs/supported-platforms.md)
* [More documentation](docs)

Are you a Microsoft employee interested in fuzzing?  Join us on Teams at [Fuzzing @ Microsoft](https://aka.ms/fuzzingatmicrosoft).

## Contributing

This project welcomes contributions and suggestions. Most contributions require
you to agree to a Contributor License Agreement (CLA) declaring that you have
the right to, and actually do, grant us the rights to use your contribution.
For details, visit [https://cla.microsoft.com](https://cla.microsoft.com).

When you submit a pull request, a CLA-bot will automatically determine whether
you need to provide a CLA and decorate the PR appropriately (e.g., label,
comment). Simply follow the instructions provided by the bot. You will only
need to do this once across all repositories using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/)
or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any
additional questions or comments.

## Data Collection

The software may collect information about you and your use of the software and
send it to Microsoft. Microsoft may use this information to provide services
and improve our products and services. You may [turn off the telemetry as
described in the
repository](docs/telemetry.md#how-to-disable-sending-telemetry-to-microsoft).
There are also some features in the software that may enable you and Microsoft
to collect data from users of your applications. If you use these features, you
must comply with applicable law, including providing appropriate notices to
users of your applications together with a copy of Microsoft's privacy
statement. Our privacy statement is located at
https://go.microsoft.com/fwlink/?LinkID=824704. You can learn more about data
collection and use in the help documentation and our privacy statement. Your
use of the software operates as your consent to these practices.

For more information:
* [Onefuzz Telemetry Details](docs/telemetry.md)

## Reporting Security Issues

Security issues and bugs should be reported privately to the Microsoft Security
Response Center (MSRC).  For more information, please see
[SECURITY.md](SECURITY.md).
