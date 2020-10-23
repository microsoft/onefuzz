# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 1.3.3
### Fixed
* Service: Fixed exception generated when deleting repro & proxy VMs [#188](https://github.com/microsoft/onefuzz/pull/188)

## 1.3.2
### Added
* Service/Agent: Non-functional nodes are now automatically re-imaged [#154](https://github.com/microsoft/onefuzz/pull/154), [#164](https://github.com/microsoft/onefuzz/pull/164), [#30](https://github.com/microsoft/onefuzz/pull/30)
* CLI: Added more granularity for the `onefuzz reset` sub-command [#161](https://github.com/microsoft/onefuzz/pull/161), [#182](https://github.com/microsoft/onefuzz/pull/182)
* Deployment/Agent: Now includes AFL++ [#7](https://github.com/microsoft/onefuzz/pull/7)
* Deployment/Agent: Now includes Radamsa for Windows [#143](https://github.com/microsoft/onefuzz/pull/143)
* CLI: The `onefuzz status top` TUI now allows filtering based on job ID, project, or name [#152](https://github.com/microsoft/onefuzz/pull/152)

### Changed
* Service: Nodes no longer have to wait for the scaleset to finish setup before being able to fuzz [#144](https://github.com/microsoft/onefuzz/pull/144)
* Agent: Agent now only notifies the service about its current state upon state change [#175](https://github.com/microsoft/onefuzz/pull/175) 
* Service: Task error messages now limit the STDOUT and STDERR to the last 4096 bytes [#170](https://github.com/microsoft/onefuzz/pull/170)
* Service: Replaced custom queue based event loop with timers [#160](https://github.com/microsoft/onefuzz/pull/160), [#159](https://github.com/microsoft/onefuzz/pull/159)
* Agent: Uploads that fail now report the failure earlier [#166](https://github.com/microsoft/onefuzz/pull/166)
* Agent: All timers now include automatic jitter to reduce request storms [#180](https://github.com/microsoft/onefuzz/pull/180)
* Agent: Ensemble container synchronization has been unified to once every 60 seconds (plus jitter) [#180](https://github.com/microsoft/onefuzz/pull/180)
* Agent: Upon agent failure, it will no longer incorrectly re-register and request new work.  [#150](https://github.com/microsoft/onefuzz/pull/150), [#146](https://github.com/microsoft/onefuzz/pull/146)

### Fixed
* Deployment: Addressed an issue with nested exceptions triggered during a failed deployment [#172]
(https://github.com/microsoft/onefuzz/pull/172)
* Deployment: Addressed incompatible prerequisite library warnings during deployment [#167](https://github.com/microsoft/onefuzz/pull/167)

## 1.3.1
### Added
* Testing: Added rust based libfuzzer in the end-to-end integration tests [#132](https://github.com/microsoft/onefuzz/pull/132)

### Fixed
* Agent: Always parse STDERR when generating crash reports for LibFuzzer instead of using `ASAN_OPTIONS=log_path`, which fixes crash reports from non-sanitizer based crashes. [#131](https://github.com/microsoft/onefuzz/pull/131)
* Deployment: Added data-migration script to fix notifications for pre-release installs [#135](https://github.com/microsoft/onefuzz/pull/135)

## 1.3.0
### Added
* Agent: Crash reports for LibFuzzer now attempts to parse STDERR in addition to `ASAN_OPTIONS=log_path`.  This enables crash reporting of go-fuzz based binaries.  [#127](https://github.com/microsoft/onefuzz/pull/127)
* Deployment: During deployment, App Insights logs can be configured to automatically export logs to the `app-insights` container in instance specific `func` storage account.  [#102](https://github.com/microsoft/onefuzz/pull/102)

### Changed
* Agent: Reduced logs sent from the agent [#125](https://github.com/microsoft/onefuzz/pull/125)
* Service: Scalesets now use [multiple placement groups](https://docs.microsoft.com/en-us/azure/virtual-machine-scale-sets/virtual-machine-scale-sets-placement-groups#placement-groups), allowing a scaleset to grow to 1000 nodes (or 600 if using a custom image).  [#121](https://github.com/microsoft/onefuzz/pull/121)

### Fixed
* Deployment: Support deploying additional platforms (such as OSX).  [#126](https://github.com/microsoft/onefuzz/pull/126)
* Service: Fixed typing error in sorting TaskEvent.  [#129](https://github.com/microsoft/onefuzz/pull/129)

## 1.2.0
### Added
* CLI/Service: Added creating and updating [Github Issues](docs/notifications/github.md) based on crash reports.  [#110](https://github.com/microsoft/onefuzz/pull/110)

### Changed
* Agent: Libfuzzer fuzzing that exits with a non-zero exit code without a resulting crashing input now mark the task as failed.  [#108](https://github.com/microsoft/onefuzz/pull/108)
* Service: The automatic variable `repro_cmd` used in [crash report notifications](docs/notifications.md) now includes '--endpoint URL' to reduce friction for users with multiple OneFuzz instances.  [#113](https://github.com/microsoft/onefuzz/pull/113)

## 1.1.0
### Added
* Agent/Service: Added the ability to automatically re-image nodes that are out-of-date [#35](https://github.com/microsoft/onefuzz/pull/35)
* Deployment: Added data-migration scripts for pre-release installs [#12](https://github.com/microsoft/onefuzz/pull/12)
* SDK/CLI: Added more `onefuzz debug` sub-commands to support debugging tasks [#95](https://github.com/microsoft/onefuzz/pull/95)
* Agent: Added machine_id and version to log messages [#94](https://github.com/microsoft/onefuzz/pull/94)
* Service: Errors in creating Azure Devops work items from reports now mark the task as failed [#77](https://github.com/microsoft/onefuzz/pull/77)
* Service: The nodes executing a task are now included when fetching details for a task (such as `onefuzz tasks get $TASKID`)  [#54](https://github.com/microsoft/onefuzz/pull/54)
* SDK: Added example [Azure Functions](https://azure.microsoft.com/en-us/services/functions/) that uses the SDK [#56](https://github.com/microsoft/onefuzz/pull/56)
* SDK/CLI: Added the ability to execute debugger commands automatically during `repro` [#39](https://github.com/microsoft/onefuzz/pull/39)
* CLI: Added documentation of CLI sub-command arguments (used to describe `afl_container` in AFL templates [#10](https://github.com/microsoft/onefuzz/pull/10)
* Agent: Added `ONEFUZZ_TARGET_SETUP_PATH` environment variable that indicates the path to the task specific setup container on the fuzzing nodes [#15](https://github.com/microsoft/onefuzz/pull/15)
* CICD: Use [sccache](https://github.com/mozilla/sccache) to speed up build times [#47](https://github.com/microsoft/onefuzz/pull/47)
* SDK: Added end-to-end [integration test script](src/cli/examples/integration-test.py) to verify full fuzzing pipelines [#46](https://github.com/microsoft/onefuzz/pull/46)
* Documentation: Added definitions for [pool](docs/terminology.md#pool), [node](docs/terminology.md#node), and [scaleset](docs/terminology.md#scaleset) [#17](https://github.com/microsoft/onefuzz/pull/17)

### Changed
* Agent/Service: Refactored state management for on-vm supervisors [#96](https://github.com/microsoft/onefuzz/pull/96)
* Agent: Added 'done' semaphore to the agent to prevent agent from fetching additional work once the node should be reset.  [#86](https://github.com/microsoft/onefuzz/pull/86)
* Agent: Nodes now sleep longer between checking for new work.  [#78](https://github.com/microsoft/onefuzz/pull/78)
* Agent: The task execution clock is now started once the task is in the 'setting up' state [#82](https://github.com/microsoft/onefuzz/pull/82)
* Service: Drastically reduced logs sent to App Insights from third-party libraries [#63](https://github.com/microsoft/onefuzz/pull/63)
* Agent/Service: Added the ability to upgrade out-of-date VMs upon requesting new tasking [#35](https://github.com/microsoft/onefuzz/pull/35)
* CICD: Non-release builds now include the GIT hash in the versions and `localchanges` if built locally with uncommited code.  [#58](https://github.com/microsoft/onefuzz/pull/58)
* Agent: [Command replacements](docs/command-replacements.md) now use absolute rather than relative paths.  [#22](https://github.com/microsoft/onefuzz/pull/22)

### Fixed
* CLI: Fixed issue using `onefuzz template stop` which would improperly stop jobs that had the same 'name' but different 'project' values.  [#97](https://github.com/microsoft/onefuzz/pull/97)
* Agent: Fixed input marker expansion (used in AFL templates related to handling `@@`).  [#87](https://github.com/microsoft/onefuzz/pull/97)
* Service: Errors generated after the task shutdown has started are ignored.  [#83](https://github.com/microsoft/onefuzz/pull/83)
* Agent: Instance specific tools now download and run on windows nodes as expected [#81](https://github.com/microsoft/onefuzz/pull/81)
* CLI: Using `--wait_for_running` in `onefuzz template` jobs now properly waits for tasks to launch before exiting [#84](https://github.com/microsoft/onefuzz/pull/84)
* Service: Handled more Azure Devops notification errors [#80](https://github.com/microsoft/onefuzz/pull/80)
* Agent: WSearch service is now properly disabled by default on Windows VMs [#67](https://github.com/microsoft/onefuzz/pull/67)
* Service: Properly deletes `repro` VMs [#36](https://github.com/microsoft/onefuzz/pull/36)
* Agent: Supervisor now flushes logs to appinsights upon exit [#21](https://github.com/microsoft/onefuzz/pull/21)
* Agent: Task specific setup script failures now properly get recorded as a failed task and trigger the node to be re-imaged [#24](https://github.com/microsoft/onefuzz/pull/24)


## 1.0.0
### Added
* Initial public release
