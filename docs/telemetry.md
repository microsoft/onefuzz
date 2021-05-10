# Telemetry

Onefuzz reports two types of telemetry, both via
[AppInsights](https://docs.microsoft.com/en-us/azure/azure-monitor/app/app-insights-overview).

1. Onefuzz records fully featured attributable data is to a user-owned
   [AppInsights](https://docs.microsoft.com/en-us/azure/azure-monitor/app/app-insights-overview)
   instance. This goal of this information is to enable users to perform
   detailed analysis of their fuzzing tasks.
1. Onefuzz reports non-attributable minimal set of runtime statistics to
   Microsoft via a Microsoft managed
   [AppInsights](https://docs.microsoft.com/en-us/azure/azure-monitor/app/app-insights-overview)
   instance. The goal is to provide insight to the efficacy of OneFuzz and
   fuzzing engines used in OneFuzz. Information regarding the the users of a
   OneFuzz instance, any applications under test, or any bug details found via
   fuzzing is not intended to be recorded in in this telemetry.

## Who owns OneFuzz Resources

For the purposes of this document, a "OneFuzz instance" is a user-deployed
install of OneFuzz in that user's
[Azure Subscription](https://docs.microsoft.com/en-us/azure/cloud-adoption-framework/decision-guides/subscriptions/).

The user owns and manages all resources used for OneFuzz, including the fuzzing
nodes. Onefuzz supports both "managed" nodes, where OneFuzz orchestrates the
lifecycle of the fuzzing nodes via
[Azure VM Scale Sets](https://docs.microsoft.com/en-us/azure/virtual-machine-scale-sets/overview),
and "unmanaged" nodes, where users provide compute however they wish (be that
on-premise hardware, third-party clouds, etc).

## How telemetry is collected

All telemetry is gathered from two places, the agents that run within fuzzing
nodes and the service API running in the Azure Functions instance.

1. The rust library [onefuzz::telemetry](../src/agent/onefuzz-telemetry/src/lib.rs)
   provides a detailed set of telemetry types, as well as the function
   `can_share_with_microsoft`, which gates if a given telemetry field should be
   sent to the Microsoft central telemetry instance.
1. The Python library
   [onefuzzlib.telemetry](../src/api-service/__app__/onefuzzlib/telemetry.py)
   provides a filtering mechanism to identify a per-object set of filtering
   records. Each ORM backed table provides a mechanism to identify the field
   should be sent to the Microsoft central telemetry instance. Example: The
   [onefuzzlib.jobs.Job.telemetry_include](../src/api-service/__app__/onefuzzlib/jobs.py)
   implementation describes the set of fields that are to be recorded.

These mechanisms ensure that any only fields intended to be recorded are sent to
the central telemetry service.

## How to disable sending telemetry to Microsoft

Remove `ONEFUZZ_TELEMETRY` in the
[Application settings](https://docs.microsoft.com/en-us/azure/azure-functions/functions-how-to-use-azure-function-app-settings#settings)
of the Azure Functions instance in the OneFuzz instance created during
deployment.

Users are reminded of how to disable the telemetry during each OneFuzz
deployment to Azure.

## Data sent to Microsoft

The following describes the information sent to Microsoft if telemetry is enabled.

### Definitions of common data types

The following are common data types used in multiple locations:

* Instance ID - A randomly generated GUID used to uniquely identify an instance of OneFuzz
* Task ID - A randomly generated GUID used to uniquely identify a fuzzing task.
* Job ID - A randomly generated GUID used to uniquely identify a job.
* Machine ID - A GUID used to identify the machine running the task. When run in
  Azure, this is the
  [VM Unique ID](https://azure.microsoft.com/en-us/blog/accessing-and-using-azure-vm-unique-id/).
  When fuzzing is run outside of Azure, this is a randomly generated GUID
  created once per node.
* Scaleset ID - A randomly generated GUID used to uniquely identify a VM
  scaleset.
* Task Type - The type of task being executed. Examples include
  `generic_crash_report` or `libfuzzer_coverage`. For a full list, see the enum
  [TaskType](../src/pytypes/onefuzztypes/enums.py).
* OS - An enum value describing the OS used (Currently, only Windows or Linux).
* Version - A compile-time generated string that specifies the OneFuzz version number based on [CURRENT\_RELEASE](../CURRENT_RELEASE) and the sha-1 git revision (See [example](../src/agent/onefuzz-agent/build.rs)).
* Role - An enum value describing the role of the OneFuzz software in use.  Examples include `Agent` or `Proxy`.  For a full list, see the enum [Role](../src/agent/onefuzz-telemetry/src/lib.rs).

### Data recorded by Agents

* Task ID
* Job ID
* Machine ID
* Task Type
* Features - A u64 representing the number of 'features' in the
  [SanCov](https://clang.llvm.org/docs/SanitizerCoverage.html)coverage map for a
  libFuzzer executable.
* Covered - A u64 representing the number of 'features' in the
  [SanCov](https://clang.llvm.org/docs/SanitizerCoverage.html)coverage map for a
  libFuzzer executable that were exercised during fuzzing.
* Rate - A float64 that is calculated as `(Covered / Features)`.
* Count - Number of executions done by the fuzzing task.
* ExecsSecond - The rate of executions per second.
* WorkerID - For fuzzers that run multiple copies concurrently on a single VM,
  this is differentiates telemetry between each instance on the VM.
* RunID - A randomly generated GUID used to uniquely identify the execution of a
  fuzzing target. For fuzzers that restart, such as libfuzzer, this is used to
  uniquely identify telemetry for each time the fuzzer is started.
* VirtualMemory - The amount virtual memory in use by the fuzzing task.
* PhysicalMemory - The amount of physical memory in use by the fuzzing task.
* CpuUsage - The amount of CPU in use by the fuzzing task.
* Crash Found - A flag that indicates that a crash was found.
* Crash Report Created - A flag that indicates a crash was found to be
  reproducible and a report was generated.
* Unique Crash Report Created - A flag that indicates that a crash was found to
  be reproducible and unique in the set of existing reports.
* Tool Name - A string that identifies the tool in use for generic tasks. For
  custom tools, this will record the custom tool name. Examples: In the
  [radamsa template](../src/cli/onefuzz/templates/afl.py), this is
  `{tools_dir}/radamsa` for the `generic_generator` task and `cdb.exe` for the
  `generic_analysis` task.

The following are [AFL](https://github.com/google/afl) specific:

* Mode - A string representing the mode of the AFL task. This is unique to
  parsing AFL stats, and specifies the "target_mode" that AFL is running in.
  Examples include, but are not limited to: "default", "qemu", and "persistent".
* CoveragePaths - A u64 representing paths_total in AFL stats.
* CoveragePathsFavored - A u64 representing paths_favored in AFL stats.
* CoveragePathsFound - A u64 representing paths_found in AFL stats.
* CoveragePathsImported - A u64 representing paths_imported in AFL stats.
* Coverage - A float64 representing bitmap_cvg in AFL stats.

### Data recorded by the Service

Each time the state of a job changes, the following information is recorded:

* Job ID
* State - The current state of the job. For a full list, see the enum
  [JobState](../src/pytypes/onefuzztypes/enums.py).

Each time the state of a task changes, the following information is recorded at
the service level:

* Task ID
* Job ID
* Task Type
* state: The current state of the task. For a full list, see the enum
  [TaskState](../src/pytypes/onefuzztypes/enums.py).
* VM count: The number of VMs used for the task.

Each time the state of a scaleset changes, the following information is
recorded:

* Scaleset ID
* OS
* VM SKU - The
  [Azure VM Size](https://docs.microsoft.com/en-us/azure/virtual-machines/sizes)
* Size - The number of VMs in the scalset. For a full list, see the enum
  [ScalesetState](../src/pytypes/onefuzztypes/enums.py).
* Spot Instances - A boolean representing if Spot Instances are used in a
  scaleset.

Each time the state of a pool changes, the following information is recorded:

* Pool ID - A randomly generated GUID used to uniquely identify a VM scaleset.
* OS
* State - The current state of the pool. For a full list, see the enum
  [PoolState](../src/pytypes/onefuzztypes/enums.py).
* Managed - A boolean representing if the pool is OneFuzz manages the VMs in
  use.

Each time the state of a fuzzing node changes, the following information is
recorded:

* Scaleset ID
* Machine ID
* State - the current state of the node. For a full list, see the enum
  [NodeState](../src/pytypes/onefuzztypes/enums.py).

Each time the state of a task on a node changes, the following information is
recorded:

* Task ID
* Machine ID
* State - the current state of the task on the node. For a full list, see the
  enum [NodeTaskState](../src/pytypes/onefuzztypes/enums.py).


### Data recorded by an upcoming feature

The following information is recorded for Salvo related tasks:

* InputsFuzzed - A u64 representing the count of inputs that were symbolically
  executed.
* SatConstraints - A u64 representing the count of satisfiable constraints and
  hence number new inputs generated.
* UnsatConstraints - A u64 representing the count of unsatisfiable constraints.
* AverageVarsPerConstraint - A float64 representing the average count of input
  bytes used per constraint over all of the inputs fuzzed.
* MaxConstraintVars - A u64 representing the maximum count of input bytes used
  for any single constraint.
* AverageSymexTime - A float64 representing the average time in seconds spent
  symbolically executing the program under test over all inputs fuzzed.
* MaxSymexTime - A u64 representing the maximum time in seconds spent
  symbolically executing the program under test for a single input.
* AverageSolvingTime - A float64 representing the average time in seconds spent
  solving constraints over all inputs fuzzed.
* MaxSolvingTime - A u64 representing the maximum time in seconds spent solving
  constraints for any single input.
* UniqueCodeLocationCount - A u64 representing the count of the unique code
  locations that are of interest to Salvo, e.g. a tainted instruction or branch
  target.
* AverageInstructionsExecuted - A float64 representing the average count of
  instructions that were symbolically executed over all fuzzed inputs.
* MaxInstructionsExecuted - A u64 representing the maximum count of
  instructions that were symbolically executed for any single fuzzed input.
* AverageTaintedInstructions - A float64 representing the count of instructions
  that make use of tainted input over all inputs fuzzed.
* MaxTaintedInstructions - A u64 representing the count of instructions that
  make use of tainted input for any single input.
* AverageMemoryTaintedInstructions - A float64 representing the count of
  instructions that make use of tainted input to read or write memory over all
  inputs fuzzed.
* MaxMemoryTaintedInstructions - A u64 representing the count of instructions
  that make use of tainted input to read or write memory for any single input.
* AveragePathLength - A u64 representing the count of the average constraints
  tracked for constraint solving while symbolically executing the program.
* MaxPathLength - A u64 representing the count of the maximum constraints
  tracked for constraint solving while symbolically executing the program.
* DivergenceRate - A float64 representing the ratio of inputs that did not
  branch as expected divided by the number of inputs fuzzed.
* DivergencePathLength - A u32 that indicates the length of execution path
  divergence.
* DivergencePathExpectedIndex - A u32 that indicates the expected index for
  divergence.
* DivergencePathActualIndex - A u32 that indicates the actual index for
  divergence.
* MissedInstructionCode - The Intel Instruction code for an instruction that
  was not modelled during symbolic execution but may have been input tainted.
  Examples include `Cmovs_r16_rm16` and `Movq_mm_rm64`.  For the full list, see
  [iced_x86::Code](https://docs.rs/iced-x86/1.10.3/iced_x86/enum.Code.html).
* MissedInstructionMnemonic - The Intel Instruction that was not modelled
  during symbolic execution but may have been input tainted. Examples include
  `Cmovs` and `Movq`.  For the full list, see
  [iced_x86::mnemonic](https://docs.rs/iced-x86/1.10.3/iced_x86/enum.Mnemonic.html).
* Z3ErrorCode - An error code that corresponds to an error code from Z3 when
  solving a constraint fails.  Examples include `NoParser` and
  `InvalidPattern`.  For the full list, see
  [z3_sys::ErrorCode](https://docs.rs/z3-sys/0.6.3/z3_sys/enum.ErrorCode.html)
* SymexTimeout - A u64 representing the maximum time in seconds to spend during
  symbolic execution, reported each time symbolic execution was stopped due to
  the limit.
