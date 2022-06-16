# Understanding Tasks

Tasks a unit of work that executes on a node (typically,
[Azure VM Scalesets](https://docs.microsoft.com/en-us/azure/virtual-machine-scale-sets/overview))
are made up of a handful of components, primarily including:

1. An application under test
1. Containers for use in specified contexts
   1. All tasks should have a `setup` container, which contains the application
      under test and optional a `setup.sh` or `setup.ps1` to customize the VM
      prior to fuzzing
   1. Input containers
   1. Output containers
1. Optionally a managed
   [Azure Storage Queue](https://docs.microsoft.com/en-us/azure/storage/queues/storage-queues-introduction)
   of new inputs to process (Used for coverage, crash reporting, etc)

The current task types available are:

* libfuzzer_fuzz: fuzz with a libFuzzer target
* libfuzzer_crash_report: Execute the target with crashing inputs, attempting to
  generate an informational report for each discovered crash
* libfuzzer_merge: merge newly discovered inputs with an input corpus using
  corpus minimization
* coverage: record binary block and source line coverage
* generic_analysis: perform [custom analysis](custom-analysis.md) on every
  crashing input
* generic_supervisor: fuzz using user-provided supervisors (such as AFL)
* generic_merge: merge newly discovered inputs with an input corpus using a user
  provided supervisor (such as afl-merge)
* generic_generator: use a generator to craft inputs and call the application
  under test iteratively to process them
* generic_crash_report: use a built-in debugging tool (debugapi or ptrace based)
  to rerun the crashing input, attempting to generate an informational report
  for each discovered crash

Each type of task has a unique set of configuration options available, these
include:

* target_exe: the application under test
* target_env: User specified environment variables for the target.
* target_options: User specified command line options for the target under test
* target_workers: User specified number of workers to launch on a given VM (At
  this time, only used for `libfuzzer` fuzzing tasks)
* target_options_merge: Enable merging supervisor and target arguments in
  supervisor based merge tasks
* analyzer_exe: User specified analysis tool (See:
  [Custom Analysis Tasks](custom-analysis.md))
* analyzer_env: User specified environment variables for the analysis tool
* analyzer_options: User specified command line options for the analysis tool
* generator_exe: User specified generator (such as radamsa.exe). The generator
  tool must exist in the task specified `generator` container
* generator_env: User specified environment variables for the generator tool
* generator_options: User specified command line options for the generator tool
* supervisor_exe: User specified generator (such as afl)
* supervisor_env: User specified environment variables for the supervisor
* supervisor_options: User specified command line options for the supervisor
* supervisor_input_marker: Marker to specify the path to the filename for
  supervisors (Example: for AFL and AFL++, this should be '@@')
* stats_file: Path to the fuzzer's stats file
* stats_format: Format of the fuzzer's stats file
* input_queue_from_container: Container name to monitor for new changes.
* rename_output: Rename generated inputs to the sha256 of the input (used during
  generator tasks)
* wait_for_files: For supervisor tasks (such as AFL), do not execute the
  supervisor until input files are available in the `inputs` container.

See [task definitions](../src/api-service/__app__/onefuzzlib/tasks/defs.py) for
implementation level details on the types of tasks available.


## Environment Variables
* `ONEFUZZ_TARGET_SETUP_PATH`: An environment variable set prior to launching target-specific setup scripts that defines the path to the setup container.