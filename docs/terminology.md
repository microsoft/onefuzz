# Terminology of OneFuzz

## Task

A task is the single unit of work. Example high level descriptions of tasks
include:

* "Run AFL with a given target"
* "With a given target, build a coverage map for every input file"

For more information: [Understanding Tasks](tasks.md)

## Job

A Job, at it's core, is merely an easy way to refer to a collection of tasks.

Jobs are uniquely identified by a job_id (UUID) and include the following
information:

* project (example: MSEdge)
* name (example: png_parser)
* build (example: 3529725.3)

## Template

A template is a pre-configured job with a set of tasks that include the most
common configurations for a given fuzz job. Templates are analogous to playbooks
or recipes, entirely built on top of the SDK. The templates can be recreated as
scripts calling the SDK or by executing the CLI.

As an example, the 'libfuzzer basic' template includes the following tasks:

* Fuzzing (Actually perform the fuzzing tasks)
* Crash Reporting (evaluate each crash for reproducibility and generating a
  consumable report)
* Coverage Reporting (evaluate every input for code coverage in the application
  under test)

At this time, templates are statically defined. In the future, OneFuzz will
allow the owner of a OneFuzz instance to manage their own templates, allowing
central management of how fuzzing tasks are defined.

## Repro

Repro is short for 'reproduction VMs'. These VMs are created on-demand to enable
debugging a crash in the same environment used for fuzzing over an SSH tunnel.
The repro VM creation automation includes downloading the task data related to
the crash, executing any setup scripts, and launching the application under test
within a debugger (`cdb -server` on Windows and `gdbserver` on Linux).

At this time, the automatic-debugger connect is functional only for file-based
fuzzing targets (such as libfuzzer or AFL), however users can connect into VMs
directly via SSH or RDP (Windows only) and have total control of the VM.

## Container

An
[Azure Blob Storage Container](https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blobs-introduction).
Each fuzzing task has a set of required and potentially optional containers that
are used in a specific context.

[More info on Containers](containers.md)

## Pool

A pool is a group of nodes to run [Tasks](#task).  Pools enable users to
specify which group of [nodes](#node) their task should run on.

Pools are defined by:
* `name`: the Name of the pool
* `os`: The operating system of the node (`linux` or `windows`)
3. `arch`: The CPU architecture of the nodes (only `x86_64` for now)
4. `managed`: Is the pool made up of OneFuzz managed [scalesets](#scaleset)

## Scaleset

A scaleset is an [Azure Virtual Machine
Scaleset](https://docs.microsoft.com/en-us/azure/virtual-machine-scale-sets/overview),
of which the entire lifecycle is managed by OneFuzz.  All of the VMs in the
scaleset are automatically setup to connect to the OneFuzz instance as a
[Node](#node).

Scalesets can run on almost any [Azure VM
Image](https://docs.microsoft.com/en-us/azure/virtual-machines/linux/cli-ps-findimage)
(use the URN for the image) or [user-provided OS images](custom-images.md).

## Node

A single compute host to run tasks.  Right now, these are only VMs in a
[scaleset](#scaleset), though support for on-prem or third-party cloud-hosted
nodes will be available in the future.
