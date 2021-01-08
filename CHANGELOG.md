# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 1.11.0
### Added 
* Service: Added support for sharding corpus storage accounts using "Premium" storage accounts for improved IOPs.  [#334](https://github.com/microsoft/onefuzz/pull/334)
* CLI/Service/Agent: Added the ability to optionally colocate multiple compatible tasks on a single machine. The coverage and crash reporting tasks in the LibFuzzer template make use of this functionality by default. [#402](https://github.com/microsoft/onefuzz/pull/402)
* CLI: Added `onefuzz debug log tail` which enables continuously following Application Insights query results.  [#401](https://github.com/microsoft/onefuzz/pull/401)
* CLI/Agent: Support verifying LibFuzzer targets at the start of a task using `-help=1`, which will enable identifying non-functional LibFuzzer targets.  [#381](https://github.com/microsoft/oneufzz/pull/381)
* CLI/Agent: Support specifying whether to log a warning or fail the task when a LibFuzzer target exits with a non-zero status code (without also generating a crashing input).  [#381](https://github.com/microsoft/onefuzz/pull/381)
* Agent: The STDOUT and STDERR for the supervisors and generators are now logged to Application Insights.  [#400](https://github.com/microsoft/onefuzz/pull/400)
* Service: Enabled per-Scaleset SSH keys on Windows VMs, similar to existing Linux support, enabling `onefuzz debug node ssh` to both Windows and Linux nodes.  [#390](https://github.com/microsoft/onefuzz/pull/390)
* Agent: Support ASAN odr-violation results.  [#380](https://github.com/microsoft/onefuzz/pull/380)
* CLI/Service/Agent: Added the ability add SSH keys to nodes within scalesets.  [#441](https://github.com/microsoft/onefuzz/pull/441)
* CLI: Added support for multi-tenant authentication.  [#346](https://github.com/microsoft/onefuzz/pull/346)

### Changed
* Service: Updating outdated nodes is now limited to 500 nodes at a time. [#397](https://github.com/microsoft/onefuzz/pull/397)
* Service: Restrict agent from accessing API endpoints not specific to the agent.  [#404](https://github.com/microsoft/onefuzz/pull/404)
* Service: Increased Azure Functions runtime timeout to 15 minutes.  [#384](https://github.com/microsoft/onefuzz/pull/384)
* Deployment/Agent: Updated AFL++ to 3.00c.  [#393](https://github.com/microsoft/onefuzz/pull/393)
* Agent: Added randomized initial jitter to agent heartbeats, which reduce API query storms when launching large number of nodes concurrently.  [#387](https://github.com/microsoft/onefuzz/pull/387)

### Fixed
* CLI/Agent: Add support to verify LibFuzzer targets execute correctly at the start of a task using `-help=1`.  [#381](https://github.com/microsoft/onefuzz/pull/381)
* Service: Re-enable API endpoint used by `onefuzz nodes update`.  [#412](https://github.com/microsoft/onefuzz/pull/412)
* Agent: Addressed a race condition in LibFuzzer coverage analysis without initial seeds.  [#403](https://github.com/microsoft/onefuzz/pull/403)
* Agent: Prevent supervisor that fatally exits from processing additional new tasks.  [#378](https://github.com/microsoft/onefuzz/pull/378)
* Agent: Address issues handling LibFuzzer targets that produce non-UTF8 output to STDERR.  [#379](https://github.com/microsoft/onefuzz/pull/379)

## 1.10.0
### Added
* CLI: Added `libfuzzer merge` job template, which enables running performing libfuzzer input minimization as a batch operation.  [#282](https://github.com/microsoft/onefuzz/pull/282)
* CLI/Service: Added the instance-specific Application Insights telemetry key to `onefuzz info get`, which will enable logging to the instance specific application insights from the SDK.  [#353](https://github.com/microsoft/onefuzz/pull/353)
* Agent: Added support for parsing AddressSanitizer `CHECK failed` entries, which can occur during large amounts of memory corruption.  [#358](https://github.com/microsoft/onefuzz/pull/358)
* Agent/Service: Added support for parsing the ASAN "scariness" score and description when `print_scariness=1` in `ASAN_OPTIONS`.  [#359](https://github.com/microsoft/onefuzz/pull/359)

### Changed
* Agent: Mark tasks as failed if the application under test generates an ASAN log file that the agent is unable to parse.  [#351](https://github.com/microsoft/onefuzz/pull/351)
* Agent: Updated the `libfuzzer_merge` task to merge pre-existing inputs in a single pass.  [#282](https://github.com/microsoft/onefuzz/pull/282)
* CLI: Clarified the error messages when prefix-expansion fails.  [#342](https://github.com/microsoft/onefuzz/pull/342)
* Service: Rendered `pydantic` models as JSON when logging to prevent `error=None` from showing up in the error logs.  [#350](https://github.com/microsoft/onefuzz/pull/350)
* Deployment: Pinned the version of pyOpenssl to the version used by multiple Azure libraries.  [#348](https://github.com/microsoft/onefuzz/pull/348)
* CLI/Service: (PREVIEW FEATURE) Multiple updates to job template management.  [#354](https://github.com/microsoft/onefuzz/pull/354), [#360](https://github.com/microsoft/onefuzz/pull/360), [#361](https://github.com/microsoft/onefuzz/pull/361)

### Fixed
* Agent: Fixed issue preventing the supervisor from notifying the service on some state changes. [#337](https://github.com/microsoft/onefuzz/pull/337)
* Deployment: Fixed a regression in retrying password creation during deployment [#338](https://github.com/microsoft/onefuzz/pull/338)
* Deployment: Fixed uploading tools when rolling back deployments. [#347](https://github.com/microsoft/onefuzz/pull/347)

## 1.9.0
### Added
* CLI/Service: Added [Service-Managed Job Templates](docs/declarative-templates.md) as a preview feature.  Enable via `onefuzz config --enable_feature job_templates`.  [#226](https://github.com/microsoft/onefuzz/pull/296)
* Service/agent: Added internal support for unmanaged nodes.  This paves the way for _bring your own compute_ for fuzzing.  [#318](https://github.com/microsoft/onefuzz/pull/318)
* CLI: Added `onefuzz debug` subcommands to simplify coverage and fuzzing performance for libFuzzer jobs from Application Insights.  [#325](https://github.com/microsoft/onefuzz/pull/325)
* Service: Information about the user responsible for creating jobs and repro VMs is now associated with the Job and Repro VMs.  [#327](https://github.com/microsoft/onefuzz/pull/327)

### Changed
* Deployment: `deploy.py` now automatically retries on failure when deploying the Azure Function App.  [#330](https://github.com/microsoft/onefuzz/pull/330)

### Fixed
* Service: Address multiple minor issues previously hidden by function decorators used for caching.  [#322](https://github.com/microsoft/onefuzz/pull/322)
* Agent: Fixed libFuzzer coverage support for internal builds of MSVC [#324](https://github.com/microsoft/onefuzz/pull/324)
* Agent: Address issue preventing instance-wide setup scripts from executing in some cases.  [#331](https://github.com/microsoft/onefuzz/pull/331)

## 1.8.0

### Added
* CLI/Service: Added [Event-based webhooks](docs/webhooks.md). [#296](https://github.com/microsoft/onefuzz/pull/296)
* Service: Information about the user responsible for creating tasks is now associated with the tasks (this information is available in the task related event webhooks). [#303](https://github.com/microsoft/onefuzz/pull/303)

### Changed
* Contrib: Azure Devops deployment pipeline uses the `--upgrade` feature added in 1.7.0. [#304](https://github.com/microsoft/onefuzz/pull/304)

### Fixed
* Service: Fixed setting `target_workers`, used to configure the number of concurrent libfuzzer workers within a task. [#305](https://github.com/microsoft/onefuzz/pull/305)

## 1.7.0
### Added
* Deployment: `deploy.py` now takes `--upgrade` to enable simplify upgrading deployments.  For now, this skips assignment of the managed identity role which only needs to be done on installation. [#271](https://github.com/microsoft/onefuzz/pull/271)
* CLI: Added Application Insights debug CLI. See `onefuzz debug logs` [#281](https://github.com/microsoft/onefuzz/pull/281)
* CLI: Added unique_inputs to the default container types for `onefuzz reset --containers` and `onefuzz containers reset`.  [#290](https://github.com/microsoft/onefuzz/pull/290)
* CLI: Added `onefuzz debug node` to enable debugging a node in a scaleset without having to specify the scaleset.  [#298](https://github.com/microsoft/onefuzz/pull/289)

### Changed
* Service: When shutting down an individual scaleset, all of the nodes in the scaleset are now marked for shutdown.  [#252](https://github.com/microsoft/onefuzz/pull/252)
* Service: The scaleset service principal IDs are now cached as part of the respective Scaleset object [#255](https://github.com/microsoft/onefuzz/pull/255)
* Service: The association from nodes that ran a task are now kept until the node is reimaged, enabling easily connecting to the node that ran a task after task completion.  [#273](https://github.com/microsoft/onefuzz/pull/273)
* Deployment: Pinned `urllib3` version due to an incompatible new release [#292](https://github.com/microsoft/onefuzz/pull/292)
* CLI: Removed calls to `containers.list`, significantly improving job template creation performance.  [#289](https://github.com/microsoft/onefuzz/pull/289)
* Service: No longer use HTTP 404 response codes during agent registration. [#287](https://github.com/microsoft/onefuzz/pull/287)
* Agent: Heartbeats are now only sent as part of the execution loop. [#283](https://github.com/microsoft/onefuzz/pull/283)
* Service: Refactored handlers for agent events, including much more detailed logging.  [#261](https://github.com/microsoft/onefuzz/pull/261)
* Deployment: Prevent users from enabling public access ton containers.  [#300](https://github.com/microsoft/onefuzz/pull/300)

### Fixed
* Service: Fixed libfuzzer_merge tasks [#240](https://github.com/microsoft/onefuzz/pull/240)
* Service: Fixed an issue where scheduled tasks waiting in the queue for longer than 7 days would never get scheduled. [#259](https://github.com/microsoft/onefuzz/pull/259)
* Service: Removed stale Node references from scalesets [#275](https://github.com/microsoft/onefuzz/pull/275)
 
## 1.6.0
### Added
* Service: The service now auto-scales the number of Azure Functions instances as needed [#238](https://github.com/microsoft/onefuzz/pull/238)
* CLI/Service/Agent: Added the ability to configure ensemble synchronization interval (including disabling ensemble altogether) [#229](https://github.com/microsoft/onefuzz/pull/229)
* Contrib: Added sample Azure Devops pipeline to maintain instances of OneFuzz [#233](https://github.com/microsoft/onefuzz/pull/233)
* Deployment: Added utility to create CLI application registrations [#236](https://github.com/microsoft/onefuzz/pull/236)
* Deployment/Service/Agent: Added a per-instance uniquely generated UUID to telemetry (see [docs/telemetry.md](docs/telemetry.md) for more information) [#245](https://github.com/microsoft/onefuzz/pull/245)

### Changed
* CLI: The CLI now internally caches container authorization tokens [#224](https://github.com/microsoft/onefuzz/pull/224)
* Service: Moved to using user-assigned managed identities for Scalesets [#219](https://github.com/microsoft/onefuzz/pull/219)
* Agent: Added stdout to azcopy error logs [#247](https://github.com/microsoft/onefuzz/pull/247)
* Service: Increased function timeouts to 5 minutes

## 1.5.0
### Added
* CLI/Service: Added the ability to prevent a VM from getting reset in order to debug tasks [#201](https://github.com/microsoft/onefuzz/pull/201)
* SDK: Add examples directory to the python package [#216](https://github.com/microsoft/onefuzz/pull/216)
* Agent: Added connection resiliency via automatic retry (with backoff) throughout the agent [#153](https://github.com/microsoft/onefuzz/pull/153)
* Deployment: Added the ability to log the application passwords during registration [#214](https://github.com/microsoft/onefuzz/pull/214)
* Agent: Libfuzzer Coverage metrics are now reported after the batch processing phase [#218](https://github.com/microsoft/onefuzz/pull/218)
* Deployment: Added a utility to assign scalesets to roles [#185](https://github.com/microsoft/onefuzz/pull/185)
* Contrib: Added a utility to automate deployment of new releases of OneFuzz via Azure Devops pipelines [#208](https://github.com/microsoft/onefuzz/pull/208)

### Fixed
* Agent: Addressed a race condition syncing input seeds [#204](https://github.com/microsoft/onefuzz/pull/204)

### Changed
* Agent: Instead of ignoring all AVs during libfuzzer coverage processing, stop on second-chance AVs [#210](https://github.com/microsoft/onefuzz/pull/210)
* Agent: During libfuzzer coverage, disable default symbol paths unless `_NT_SYMBOL_PATH` is set via `target_env`.  [#222](https://github.com/microsoft/onefuzz/pull/222)

## 1.4.0
### Added
* CLI: Added `onefuzz containers reset` to delete containers by type en masse.  [#198](https://github.com/microsoft/onefuzz/pull/198), [#202](https://github.com/microsoft/onefuzz/pull/202)
* Agent: Added missing approved telemetry as to tool names & crash report identification. [#203](https://github.com/microsoft/onefuzz/pull/203)

### Changed
* Service: Enabled log sampling at the service at 20 items per second.  [#174](https://github.com/microsoft/onefuzz/pull/174)

### Fixed
* Service: Fixed multiple bugs in the service, including an exception due to invalid format string proxy or repro VM creation [#206](https://github.com/microsoft/onefuzz/pull/206)

## 1.3.4
### Fixed
* CLI: Fixed incorrect resetting of granularly selected components introduced in 1.3.3 [#193](https://github.com/microsoft/onefuzz/pull/193)
* Service: Fixed rate-limiting issues requesting MSI and Storage Account tokens [#195](https://github.com/microsoft/onefuzz/pull/195)

### Changed
* Service: Moved the SDK to use the same `pydantic` models as the service in request generation [#191](https://github.com/microsoft/onefuzz/pull/191)
* Service: Improved performance of container validation [#196](https://github.com/microsoft/onefuzz/pull/196)

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
