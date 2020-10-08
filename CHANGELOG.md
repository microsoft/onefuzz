# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
