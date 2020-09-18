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