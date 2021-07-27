# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 2.29.0
### Added
* Service: PII is now removed from Jobs, Tasks, and Repros after 18 months.  [#1051](https://github.com/microsoft/onefuzz/pull/1051)
* Service: Unused notifications are now removed after 18 months.  [#1051](https://github.com/microsoft/onefuzz/pull/1051)

### Changed
* Service: SignalR events are routed through an Azure Storage Queue to prevent SignalR outages from impacting the entire service.  [#1100](https://github.com/microsoft/onefuzz/pull/1100), [#1102](https://github.com/microsoft/onefuzz/pull/1102)
* Service: Functionality used prior to 1.0.0 for assigning tasks to VMs rather than Pools is no longer supported.  [#1105](https://github.com/microsoft/onefuzz/pull/1105)
* Service: The `coverage` and `generic_generator` tasks now verify `{input}` is used in `target_env` or `target_options`.  [#1106](https://github.com/microsoft/onefuzz/pull/1106)

### Fixed
* Service: Fixed an issue reimaging old nodes with `debug_keep_node` set.  [#1103](https://github.com/microsoft/onefuzz/pull/1103)
* Service: Fixed an issue authenticating to Azure services.  [#1099](https://github.com/microsoft/onefuzz/pull/1099)
* Service: Fixed an issue preventing Pools and Scalesets set to `shutdown` from being set to `halt`.  [#1104](https://github.com/microsoft/onefuzz/pull/1104)

## 2.28.0
### Added
* CLI: Added the ability to remove existing container notifications upon creating a notification integration.  [#1084](https://github.com/microsoft/onefuzz/pull/1084)
* CLI/Documentation: Added an example `generic_analysis` task that demonstrates collecting LLVM source-based coverage.  [#1072](https://github.com/microsoft/onefuzz/pull/1072)
* Supervisor: Added service-interaction resiliency for node commands.  [#1098](https://github.com/microsoft/onefuzz/pull/1098)

### Changed
* Agent/Supervisor/Proxy: Addressed multiple new `cargo-clippy` warnings.  [#1089](https://github.com/microsoft/onefuzz/pull/1089)
* Agent: Added more context to errors in generator tasks.  [#1094](https://github.com/microsoft/onefuzz/pull/1094)
* Agent: Added support for ASAN runtime identification of format string bugs.  [#1093](https://github.com/microsoft/onefuzz/pull/1093)
* Agent: Added verification that `{input}` is provided to the application under test via `target_env` or `target_options`.  [#1097](https://github.com/microsoft/onefuzz/pull/1097)
* Agent: Continued development related to upcoming features.  [#1090](https://github.com/microsoft/onefuzz/pull/1090), [#1091](https://github.com/microsoft/onefuzz/pull/1091)
* CLI/Service: Updated multiple first-party and third-party Python dependencies.  [#1086](https://github.com/microsoft/onefuzz/pull/1086)
* CLI: Changed job templates to replace existing notifications for the unique report container.  [#1084](https://github.com/microsoft/onefuzz/pull/1084)
* Service: Added more context to Azure DevOps errors.  [#1082](https://github.com/microsoft/onefuzz/pull/1082)
* Service: Notification secrets are now deleted from Azure KeyVault upon notification deletion.  [#1085](https://github.com/microsoft/onefuzz/pull/1085)

### Fixed
* Agent: Fixed an issue logging ASAN output upon ASAN log parse errors.  [#1092](https://github.com/microsoft/onefuzz/pull/1092)
* Agent: Fixed issues handling non-UTF8 output from applications under test.  [#1088](https://github.com/microsoft/onefuzz/pull/1088)

## 2.27.0
### Changed
* Agent: Batch processing results are now saved after every 10 executions.  [#1076](https://github.com/microsoft/onefuzz/pull/1076)
* Service: Optimized `file_added` event queueing by avoiding unnecessary Azure queries.  [#1075](https://github.com/microsoft/onefuzz/pull/1075)
* Agent: Optimized directory change monitoring.  [#1078](https://github.com/microsoft/onefuzz/pull/1078)
* Supervisor: Optimized agent monitoring.  [#1080](https://github.com/microsoft/onefuzz/pull/1080)

## 2.26.1
### Fixed
* CLI: Fixed an issue handling long-running requests.  [#1068](https://github.com/microsoft/onefuzz/pull/1068)
* CLI/Service: Fixed an issue related to upcoming features.  [#1067](https://github.com/microsoft/onefuzz/pull/1067)
* CLI: Fixed an issue handling `target_options` for libFuzzer jobs.  [#1066](https://github.com/microsoft/onefuzz/pull/1066)

## 2.26.0
### Added
* Supervisor: Added a `panic` handler to record supervisor failures. [#1062](https://github.com/microsoft/onefuzz/pull/1062)

### Changed
* Agent: Added more context to file upload errors.  [#1063](https://github.com/microsoft/onefuzz/pull/1063)
* CLI: Made errors locating `azcopy` more clear. [#1061](https://github.com/microsoft/onefuzz/pull/1061)

### Fixed
* Service: Fixed an issue where long-lived VM scaleset instances could get reimaged with out-of-date VM setup scripts.  [#1060](https://github.com/microsoft/onefuzz/pull/1060)
* Service: Fixed an issue where VM setup script updates were not always pushed.  [#1059](https://github.com/microsoft/onefuzz/pull/1059)

## 2.25.1
### Fixed
* Service: Fixed an issue detecting and reimaging failed nodes.  [#1054](https://github.com/microsoft/onefuzz/pull/1054)
* Service: Fixed an issue with the supervisor restarting too quickly.  [#1055](https://github.com/microsoft/onefuzz/pull/1055)

## 2.25.0
### Added
* Agent: Added `minimized_stack_function_lines` and `minimized_stack_function_lines_sha256` to crash reports.  [#993](https://github.com/microsoft/onefuzz/pull/993)
* CLI/Service: Added `timestamp` to `Notification` objects.  [#1043](https://github.com/microsoft/onefuzz/pull/1043)
* Service: Added the [scaleset\_resize\_scheduled](docs/webhook_events.md#scaleset_resize_scheduled) event.  [#1047](https://github.com/microsoft/onefuzz/pull/1047)
* Service: Added `pool_id` to `Node` objects. [#1049](https://github.com/microsoft/onefuzz/pull/1049)

### Changed
* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies.  [#1040](https://github.com/microsoft/onefuzz/pull/1040), [#1052](https://github.com/microsoft/onefuzz/pull/1052)
* CLI/Deployment/Service: Updated multiple first-party and third-party Python dependencies.  [#922](https://github.com/microsoft/onefuzz/pull/922), [#1045](https://github.com/microsoft/onefuzz/pull/1045)
* CLI/Service: Moved to using Pydantic built-in size validation for types. [#1048](https://github.com/microsoft/onefuzz/pull/1048)
* Service: Continued development related to upcoming features.  [#1046](https://github.com/microsoft/onefuzz/pull/1046), [#1050](https://github.com/microsoft/onefuzz/pull/1050)

### Fixed
* CLI: Fixed an issue handling column sorting in `onefuzz status top`.  [#1037](https://github.com/microsoft/onefuzz/pull/1037)
* Service: Fixed an issue adding SSH keys to Windows VMs.  [#1038](https://github.com/microsoft/onefuzz/pull/1038)

## 2.24.0
### Added
* CLI/Service: Added instance configuration that can be managed via `onefuzz instance_config`.  [#1010](https://github.com/microsoft/onefuzz/pull/1010)
* Service: Added automatic retry for Azure Devops notifications.  [#1026](https://github.com/microsoft/onefuzz/pull/1026)
* CLI/Service: Added validation to GitHub Issues integration configuration. [#1019](https://github.com/microsoft/onefuzz/pull/1019)

### Changed
* Agent/Supervisor/Proxy: Moved to `rustls` to enable running the Agent and Supervisor on Ubuntu 20.04.  [#1029](https://github.com/microsoft/onefuzz/pull/1029)
* Agent: Continued development related to upcoming features.  [#1016](https://github.com/microsoft/onefuzz/pull/1016)

### Fixed
* Agent: Fixed an issue handling invalid data during coverage collection.  [#1032](https://github.com/microsoft/onefuzz/pull/1032)
* Agent: Fixed retry logic on coverage recording failures [#1033](https://github.com/microsoft/onefuzz/pull/1033)

## 2.23.1
### Fixed
* Service: Fixed an issue preventing deletion or reimaging of nodes in some cases. [#1023](https://github.com/microsoft/onefuzz/pull/1023)

## 2.23.0
### Changed
* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies.  [#1018](https://github.com/microsoft/onefuzz/pull/1018), [#1009](https://github.com/microsoft/onefuzz/pull/1009), [#1004](https://github.com/microsoft/onefuzz/pull/1004)
* Service: Tasks running on nodes without recent heartbeats are now marked as failed due to heartbeat issues.  [#1015](https://github.com/microsoft/onefuzz/pull/1015)
* Service: Updated multiple first-party Python dependencies. [#1012](https://github.com/microsoft/onefuzz/pull/1012)

### Fixed
* Agent: Fixed an issue where `libfuzzer_fuzz` tasks on Windows that found crashes too rapidly were unable recover handles. [#1002](https://github.com/microsoft/onefuzz/pull/1002)
* Agent: Fixed an issue with the regression tasks after using the `onefuzz debug notification` commands. [#1011](https://github.com/microsoft/onefuzz/pull/1011)
* Deployment: Fixed a configuration issue reducing log retention durations.  [#1007](https://github.com/microsoft/onefuzz/pull/1007)
* Service: Fixed an issue creating GitHub Issues notifications. [#1008](https://github.com/microsoft/onefuzz/pull/1008)
* Service: Fixed an issue handling reimaging nodes that took an excessive amount of time. [#1005](https://github.com/microsoft/onefuzz/pull/1005)

## 2.22.0
### Changed
* Service: Update node and task-related log messages to ease debugging. [#988](https://github.com/microsoft/onefuzz/pull/988)
* Agent: Changed the log level for `azcopy` retry notification to `DEBUG`. [#986](https://github.com/microsoft/onefuzz/pull/986)
* Agent: Updated stack minimization regular expressions from `libclusterfuzz`. [#992](https://github.com/microsoft/onefuzz/pull/992)
* Agent: Added more context to synchronized directory errors.  [#995](https://github.com/microsoft/onefuzz/pull/995)
* Deployment: Reduced the Application Insights log retention duration to 30 days.  [#997](https://github.com/microsoft/onefuzz/pull/997)
* Agent: Improved tracking of threads during win32 debugging.  [#1000](https://github.com/microsoft/onefuzz/pull/1000)

### Fixed
* Agent: Fixed an issue using relative paths with synchronized directories.  [#996](https://github.com/microsoft/onefuzz/pull/996)
* Service: Fixed an issue creating GitHub Issues notifications [#990](https://github.com/microsoft/onefuzz/pull/990)
* CLI/Service: Fixed an issue handling `Union` fields in the `onefuzztypes` library [#982](https://github.com/microsoft/onefuzz/pull/982)
* Service: Fixed an issue handling manually-resized scalesets [#984](https://github.com/microsoft/onefuzz/pull/984)

## 2.21.0
### Added
* CLI: Added `onefuzz debug job rerun` command. [#960](https://github.com/microsoft/onefuzz/pull/960)

### Changed
* Agent: Added more context to coverage recording errors. [#979](https://github.com/microsoft/onefuzz/pull/979)
* Agent: The coverage task now retries an input in the case of coverage recording failure. [#978](https://github.com/microsoft/onefuzz/pull/978)
* Service: Nodes with the `debug_keep_node` flag will now be reimaged once the node is 7 days old. [#968](https://github.com/microsoft/onefuzz/pull/968)
* Service: Updates to scalesets can now be requested while the node is in the `resize` state.  [#969](https://github.com/microsoft/onefuzz/pull/969)

### Fixed
* Service: Fixed an issue when reimaging nodes that previously failed to reimage as expected. [#970](https://github.com/microsoft/onefuzz/pull/970)
* Service: Fixed an issue when resizing scalesets that exceed Azure VM quotas. [#967](https://github.com/microsoft/onefuzz/pull/967)
* Supervisor: Fixed an issue with refreshing service authentication tokens. [#976](https://github.com/microsoft/onefuzz/pull/976)

## 2.20.0
### Added
* Agent: Added a new `coverage` task that enables coverage analysis for both uninstrumented and Sancov targets on Linux and Windows. [#763](https://github.com/microsoft/onefuzz/pull/763)

### Changed
* Agent: Improved performance of the libFuzzer fuzzing tasks. [#941](https://github.com/microsoft/onefuzz/pull/941)
* CLI: Changed the `libfuzzer basic` job template to use the new `coverage` task.  [#763](https://github.com/microsoft/onefuzz/pull/763)
* Deployment: Added automatic retry when authorizing newly-created applications during deployment.  [#959](https://github.com/microsoft/onefuzz/pull/959)
* Supervisor: Simplified the service coordination logic and added increased context upon failure. [#963](https://github.com/microsoft/onefuzz/pull/963)

## 2.19.0
### Added
* Agent/Supervisor: Added azcopy log recording upon azcopy failure. [#945](https://github.com/microsoft/onefuzz/pull/945)
* CLI: Added `onefuzz jobs containers delete` command.  [#949](https://github.com/microsoft/onefuzz/pull/949)
* CLI: Added `onefuzz jobs containers download` command. [#953](https://github.com/microsoft/onefuzz/pull/953)

### Changed
* Agent/Service: Agents scheduled to shut down no longer wait for work prior to shutting down.  [#940](https://github.com/microsoft/onefuzz/pull/940)
* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies.  [#942](https://github.com/microsoft/onefuzz/pull/942)
* Agent: Continued deveopment related to upcoming features. [#937](https://github.com/microsoft/onefuzz/pull/937), [#929](https://github.com/microsoft/onefuzz/pull/929), [#919](https://github.com/microsoft/onefuzz/pull/919)
* CLI: Message details are now always shown in `onefuzz status top`.  [#933](https://github.com/microsoft/onefuzz/pull/933)
* CLI: Renamed template helper methods for uploading task setup files. [#926](https://github.com/microsoft/onefuzz/pull/926)
* Contrib: Updated multiple third-party Python dependencies.  [#950](https://github.com/microsoft/onefuzz/pull/950)
* Service: Tasks that are stopped without ever having started are now marked as failed.  [#935](https://github.com/microsoft/onefuzz/pull/935)
* Supervisor: Added increased context when recording supervisor failures. [#931](https://github.com/microsoft/onefuzz/pull/931)

### Fixed
* CLI/Service: Worked around a third-party dependency issue in handling Python Unions in Events.  [#939](https://github.com/microsoft/onefuzz/pull/939)
* Deployment: Fixed an authentication issue during deployment. [#947](https://github.com/microsoft/onefuzz/pull/947), [#954](https://github.com/microsoft/onefuzz/pull/954)
* Deployment: Fixed an issue limiting application creation logs.  [#952](https://github.com/microsoft/onefuzz/pull/952)
* Service: Fixed an issue deleting nodes with expired heartbeats. [#930](https://github.com/microsoft/onefuzz/pull/930)
* Service: Fixed an issue deleting nonexistent containers.  [#948](https://github.com/microsoft/onefuzz/pull/948)
* Service: Fixed an issue deleting proxies.  [#932](https://github.com/microsoft/onefuzz/pull/932)
* Service: Fixed an issue that prevented automatic migration of notification secrets to Azure KeyVault in some cases. [#936](https://github.com/microsoft/onefuzz/pull/936)
* Supervisor: Fixed an issue adding multiple SSH keys to Windows VMs. [#928](https://github.com/microsoft/onefuzz/pull/928)

## 2.18.0
### Added
* Agent: Added `setup_dir` [configuration value expansion](docs/command-replacements.md) for generator tasks. [#901](https://github.com/microsoft/onefuzz/pull/901)
* CLI: Enable specifying alternate tenant configuration via command line arguments. [#900](https://github.com/microsoft/onefuzz/pull/900)
* CLI/Service: Proxy status is now available via `onefuzz scaleset_proxy list` command. [#905](https://github.com/microsoft/onefuzz/pull/905)

### Changes
* Deployment: Moved to using Microsoft Graph `User.Read` rather than Azure AD Graph. [#894](https://github.com/microsoft/onefuzz/pull/894)
* Service: Tasks are now stopped on nodes before task related storage queues are deleted. [#801](https://github.com/microsoft/onefuzz/pull/801)
* Proxy: Proxies are automatically deployed and always available based on regions with active fuzzing scalesets. [#839](https://github.com/microsoft/onefuzz/pull/839), [#908](https://github.com/microsoft/onefuzz/pull/908), [#907](https://github.com/microsoft/onefuzz/pull/907), [#909](https://github.com/microsoft/onefuzz/pull/909), [#904](https://github.com/microsoft/onefuzz/pull/904)
* CLI: Added explanations to errors generated when parsing arguments whose values are key/value pairs. [#910](https://github.com/microsoft/onefuzz/pull/910), [#911](https://github.com/microsoft/onefuzz/pull/911)
* Agent: Continued development related to upcoming features. [#913](https://github.com/microsoft/onefuzz/pull/913), [#918](https://github.com/microsoft/onefuzz/pull/918)
* Service: Updated first-party Python libraries [#903](https://github.com/microsoft/onefuzz/pull/903)

## 2.17.0
### Added
* Documentation: Added [descriptions](docs/AADEntitites.md) for the Azure AD entities used by OneFuzz. [#896](https://github.com/microsoft/onefuzz/pull/896)
* Service: Added the [scaleset\_state\_updated](docs/webhook_events.md#scaleset_state_updated) event.  [#882](https://github.com/microsoft/onefuzz/pull/882)

### Changes
* Agent/Supervisor/Proxy: Addressed multiple new `cargo-clippy` warnings.  [#884](https://github.com/microsoft/onefuzz/pull/884)
* Agent/Supervisor/Proxy: Updated and removed third-party Rust dependencies.  [#892](https://github.com/microsoft/onefuzz/pull/892), [#873](https://github.com/microsoft/onefuzz/pull/873), [#865](https://github.com/microsoft/onefuzz/pull/865)
* Service: Improved the Python typing signatures used in the service.  [#881](https://github.com/microsoft/onefuzz/pull/881)
* Service: Updated multiple first-party and third-party Python libraries.  [#893](https://github.com/microsoft/onefuzz/pull/893), [#889](https://github.com/microsoft/onefuzz/pull/889), [#866](https://github.com/microsoft/onefuzz/pull/886), [#885](https://github.com/microsoft/onefuzz/pull/885), [#861](https://github.com/microsoft/onefuzz/pull/861), [#890](https://github.com/microsoft/onefuzz/pull/890), 
* Supervisor: The supervisor now includes the full error context upon failure. [#879](https://github.com/microsoft/onefuzz/pull/879)
* Service: Cleaned up scaleset update logs. [#880](https://github.com/microsoft/onefuzz/pull/880)
* Agent: Continued development related to upcoming features. [#874](https://github.com/microsoft/onefuzz/pull/874), [#868](https://github.com/microsoft/onefuzz/pull/868), [#864](https://github.com/microsoft/onefuzz/pull/864)
* SDK/CLI: Replaced Python based directory uploading with `azcopy sync`.  [#878](https://github.com/microsoft/onefuzz/pull/878)

### Fixed
* Service/Supervisor: Fixed an issue shrinking scalesets where idle nodes would not shut down as expected. [#866](https://github.com/microsoft/onefuzz/pull/866)
* Deployment: Fixed an issue deploying to non-Microsoft single-tenant instances. [#872](https://github.com/microsoft/onefuzz/pull/872), [#898](https://github.com/microsoft/onefuzz/pull/898)

## 2.16.0
### Aded
* Deployment: Added ability to only deploy RBAC rsources. [#818](https://github.com/microsoft/onefuzz/pull/818)
* Agent: Continued development related to upcoming features. [#855](https://github.com/microsoft/onefuzz/pull/855), [#858](https://github.com/microsoft/onefuzz/pull/858)

### Fixed
* Agent: Fixed issue where directory monitoring would fail due to `azcopy` temporary files. [#859](https://github.com/microsoft/onefuzz/pull/859)
* Service: Fixed issue where scalesets could get stuck trying to resize if also manually deleted. [#860](https://github.com/microsoft/onefuzz/pull/860)

## 2.15.0
### Added
* Agent: Added context to errors generated during [configuration value expansion](docs/command-replacements.md). [#835](https://github.com/microsoft/onefuzz/pull/835).
* CLI/Service: Added messages awaiting processing for a node to the node status API.  [#836](https://github.com/microsoft/onefuzz/pull/836)
* Agent: Continued development related to upcoming features. [#844](https://github.com/microsoft/onefuzz/pull/844), [#852](https://github.com/microsoft/onefuzz/pull/852), [#850](https://github.com/microsoft/onefuzz/pull/850), [#843](https://github.com/microsoft/onefuzz/pull/843), [#837](https://github.com/microsoft/onefuzz/pull/837), [#838](https://github.com/microsoft/onefuzz/pull/838), [#844](https://github.com/microsoft/onefuzz/pull/844)

### Changes
* Agent/Proxy/Supervisor : Updated multiple third-party Rust dependencies.  [#842](https://github.com/microsoft/onefuzz/pull/842), [#826](https://github.com/microsoft/onefuzz/pull/826), [#829](https://github.com/microsoft/onefuzz/pull/829), 
* Service/Contrib: Updated multiple Python dependencies.  [#828](https://github.com/microsoft/onefuzz/pull/828), [#827](https://github.com/microsoft/onefuzz/pull/827), [#823](https://github.com/microsoft/onefuzz/pull/823), [#822](https://github.com/microsoft/onefuzz/pull/822), [#821](https://github.com/microsoft/onefuzz/pull/821), [#847](https://github.com/microsoft/onefuzz/pull/847)
* Service: Resetting nodes no longer requires waiting for the node to acknowledge the shutdown in some cases. [#834](https://github.com/microsoft/onefuzz/pull/834)

### Fixed
* Supervisor: Fixed an issue introduced in 2.14.0 that sometimes prevents nodes from stopping processing tasks.  [#833](https://github.com/microsoft/onefuzz/pull/833)
* Service: Fixed an issue related to Azure Storage Queues being deleted while in use. [#832](https://github.com/microsoft/onefuzz/pull/832)
* Deployment: Fixed an issue where the CLI client application role was not assigned during deployment.  [#825](https://github.com/microsoft/onefuzz/pull/825)

## 2.14.0
### Added
* Contrib: Added a sample GitHub Actions workflow and an Azure DevOps Pipeline to demonstrate deploying OneFuzz jobs using CICD.  [#778](https://github.com/microsoft/onefuzz/pull/778)
* CLI/Service: Added creation timestamps to `Job`, `Node`, `Pool`, `Scaleset`, `Repro`, `Task`, and `TaskEvent` records returned by the service.  [#796](https://github.com/microsoft/onefuzz/pull/796), [#805](https://github.com/microsoft/onefuzz/pull/805), [#804](https://github.com/microsoft/onefuzz/pull/804)
* Agent/Proxy/Supervisor: Added additional context to web request failures to assist in debugging issues.  [#798](https://github.com/microsoft/onefuzz/pull/798)
* Service: Added task configuration to the [crash\_reported](docs/webhook_events.md#crash_reported) and [regression\_reported](https://github.com/microsoft/onefuzz/blob/main/docs/webhook_events.md#regression_reported) events.  [#793](https://github.com/microsoft/onefuzz/pull/793)

### Changes
* Agent: The full error context is now logged upon task failure. [#802](https://github.com/microsoft/onefuzz/pull/802)
* CLI: The `libfuzzer-dotnet` template no longer defaults to failing the task if the fuzzer exits with a non-zero status but no crash artifact.  [#807](https://github.com/microsoft/onefuzz/pull/807)
* Agent/Proxy/Supervisor: Updated multiple Rust dependencies.  [#800](https://github.com/microsoft/onefuzz/pull/800)
* Service: When multiple failures are reported for a given task, only the first failure is recorded.  [#797](https://github.com/microsoft/onefuzz/pull/797)
* Agent: Continued development related to upcoming features. [#820](https://github.com/microsoft/onefuzz/pull/820), [#816](https://github.com/microsoft/onefuzz/pull/816), [#790](https://github.com/microsoft/onefuzz/pull/790), [#809](https://github.com/microsoft/onefuzz/pull/809), [#812](https://github.com/microsoft/onefuzz/pull/812), [#811](https://github.com/microsoft/onefuzz/pull/811), [#810](https://github.com/microsoft/onefuzz/pull/810), [#794](https://github.com/microsoft/onefuzz/pull/794), [#799](https://github.com/microsoft/onefuzz/pull/799), [#779](https://github.com/microsoft/onefuzz/pull/779)

### Fixed
* Deployment: Added missing actions to the example Custom Azure Role for deployment. [#808](https://github.com/microsoft/onefuzz/pull/808)
* Service: Fixed an issue in scaleset creation with incompatible VM SKUs and VM Images. [#803](https://github.com/microsoft/onefuzz/pull/803)
* Service: Fixed an issue removing user identity information from logging to user instances.  [#795](https://github.com/microsoft/onefuzz/pull/795)

## 2.13.0
### Added
* Deployment: Allow specifying the Azure subscription to use for deployment, instead of always using the default [#774](https://github.com/microsoft/onefuzz/pull/774)

### Changed
* Agent/Supervisor: Added automatic retry when executing `azcopy`.  [#701](https://github.com/microsoft/onefuzz/pull/701)
* Service: When task setup fails, the error that caused the setup failure is now included in the Task error message.  [#781](https://github.com/microsoft/onefuzz/pull/781)
* Agent: The `libfuzzer-fuzz` task no longer queries the full local system status when only reporting process status.  [#784](https://github.com/microsoft/onefuzz/pull/784)
* Agent: The `libfuzzer-fuzz` task now limits the stderr collected to the last 1024 lines for potential failure reporting.  [#785](https://github.com/microsoft/onefuzz/pull/785)
* Agent: The `libfuzzer-fuzz` task now summarizes the executions per second and iteration counts from all of the workers on each VM.  [#786](https://github.com/microsoft/onefuzz/pull/786)
* Agent: The `libfuzzer-coverage` task no longer removes the initial copy of inputs.  [#788](https://github.com/microsoft/onefuzz/pull/788)
* Agent: Debugger scripts for extracting libFuzzer coverage are now embedded in the agent.  [#783](https://github.com/microsoft/onefuzz/pull/783)
* Agent: Continued development related to upcoming features. [#787](https://github.com/microsoft/onefuzz/pull/787), [#776](https://github.com/microsoft/onefuzz/pull/776), [#663](https://github.com/microsoft/onefuzz/pull/663)

### Fixed
* CLI: Fixed issue relating to line endings in the `libfuzzer-qemu` job template setup script. [#782](https://github.com/microsoft/onefuzz/pull/782)
* Service: Fixed backward compatibility issue in ephemeral disk support when creating scalesets.  [#780](https://github.com/microsoft/onefuzz/pull/780)
* Deployment: Fixed issue in multi-tenant deployment support. [#773](https://github.com/microsoft/onefuzz/pull/773)

## 2.12.0
### Added
* Agent: LibFuzzer tasks now include a verification step that verifies the fuzzer can test a small number of seeds at the start of the task.  [#752](https://github.com/microsoft/onefuzz/pull/752)
* Integration Tests: Added verification that no errors are logged to Application Insights during testing.  [#700](https://github.com/microsoft/onefuzz/pull/700)
* Agent/Supervisor/Service/Deployment: Added support for multi-tenant authentication.  [#746](https://github.com/microsoft/onefuzz/pull/746)
* CLI/Service: Added support for [Ephemeral OS Disks](https://docs.microsoft.com/en-us/azure/virtual-machines/ephemeral-os-disks).  [#461](https://github.com/microsoft/onefuzz/pull/461), [#761](https://github.com/microsoft/onefuzz/pull/761) 

### Changed
* Agent: Continued development related to upcoming features. [#765](https://github.com/microsoft/onefuzz/pull/765), [#762](https://github.com/microsoft/onefuzz/pull/762), [#754](https://github.com/microsoft/onefuzz/pull/754), [#756](https://github.com/microsoft/onefuzz/pull/756), [#750](https://github.com/microsoft/onefuzz/pull/750), [#744](https://github.com/microsoft/onefuzz/pull/744), [#753](https://github.com/microsoft/onefuzz/pull/753)
* Contrib: Updated multiple python dependencies.  [#764](https://github.com/microsoft/onefuzz/pull/764)
* CLI/Agent: LibFuzzer fuzzing tasks no longer default to failing the task if the fuzzer exits with a non-zero status but no crash artifact.  [#748](https://github.com/microsoft/onefuzz/pull/748)

### Fixed
* Agent/Proxy/Supervisor: Fixed issues prevent HTTPS retries.  [#766](https://github.com/microsoft/onefuzz/pull/766)
* Agent/Service/Proxy/Supervisor: Fixed logging and telemetry from the agent. [#769](https://github.com/microsoft/onefuzz/pull/769)

## 2.11.1
### Fixed
* Agent/Proxy/Supervisor: Fixed issues preventing heartbeats.  [#749](https://github.com/microsoft/onefuzz/pull/749)

## 2.11.0
### Changed
* Agent: Continued log simplification and clarification.  [#736](https://github.com/microsoft/onefuzz/pull/736), [#740](https://github.com/microsoft/onefuzz/pull/740), [#742](https://github.com/microsoft/onefuzz/pull/742)
* Agent: Prevent invalid queue messages from being ignored. [#731](https://github.com/microsoft/onefuzz/pull/731)
* Agent: Separated module and symbol names for Windows debugger-based crash reports. [#723](https://github.com/microsoft/onefuzz/pull/723)
* Deployment/Agent: Updated AFL++ to 3.11c.  [#728](https://github.com/microsoft/onefuzz/pull/728)
* CLI/Deployment: Updated Python dependencies.  [#721](https://github.com/microsoft/onefuzz/pull/721)
* Agent: Updated stack minimization regular expressions from ClusterFuzz.  [#722](https://github.com/microsoft/onefuzz/pull/722)
* Service: Removed user's identity information from logging to user instances.  [#724](https://github.com/microsoft/onefuzz/pull/724), [#725](https://github.com/microsoft/onefuzz/pull/725)
* Agent: Continued development related to upcoming features. [#699](https://github.com/microsoft/onefuzz/pull/699), [#729](https://github.com/microsoft/onefuzz/pull/729), [#733](https://github.com/microsoft/onefuzz/pull/733), [#735](https://github.com/microsoft/onefuzz/pull/735), [#738](https://github.com/microsoft/onefuzz/pull/738), [#739](https://github.com/microsoft/onefuzz/pull/739)

### Fixed
* Deployment: Worked around a race condition in service principal creation. [#716](https://github.com/microsoft/onefuzz/pull/716)
* Agent: Dotfiles are now ignored in libFuzzer-related directories.  [#741](https://github.com/microsoft/onefuzz/pull/741)

## 2.10.0
### Added
* Agent/CLI/Service: Added regression testing tasks, including enabling [git bisect using OneFuzz](docs/how-to/git-bisect-a-crash.md).  [#664](https://github.com/microsoft/onefuzz/pull/664), [#691](https://github.com/microsoft/onefuzz/pull/691)
* Agent/CLI/Service: Added call stack minimization using a [Rust port](src/agent/libclusterfuzz) of [ClusterFuzz stack trace parsing](https://github.com/google/clusterfuzz/tree/master/src/python/lib). [#591](https://github.com/microsoft/onefuzz/pull/591), [#705](https://github.com/microsoft/onefuzz/pull/705), [#706](https://github.com/microsoft/onefuzz/pull/706), [#707](https://github.com/microsoft/onefuzz/pull/707), [#714](https://github.com/microsoft/onefuzz/pull/714), [#715](https://github.com/microsoft/onefuzz/pull/715), [#719](https://github.com/microsoft/onefuzz/pull/719)
* CLI: Added `onefuzz privacy_statement` command, which displays OneFuzz's privacy statement. [#695](https://github.com/microsoft/onefuzz/pull/695)
* Agent: Added installation of the `x86` and `x86_64` Visual Studio C++ redistributable runtimes on Windows nodes.  [#686](https://github.com/microsoft/onefuzz/pull/686)

### Changed
* Agent/Proxy/Supervisor: Changed web request retry logic to include the underlying failure upon giving up retrying a request. [#696](https://github.com/microsoft/onefuzz/pull/696)
* Supervisor: Added automatic web request retry logic when communicating to the service. [#704](https://github.com/microsoft/onefuzz/pull/704)
* CLI/Service: Updated Python dependencies.  [#698](https://github.com/microsoft/onefuzz/pull/698), [#687](https://github.com/microsoft/onefuzz/pull/687)
* Supervisor: Clarified log message when the supervisor unexpectedly exits. [#685](https://github.com/microsoft/onefuzz/pull/685)
* Proxy: Simplified service communication logic. [#683](https://github.com/microsoft/onefuzz/pull/683)
* Proxy: Increased log verbosity on proxy failure. [#702](https://github.com/microsoft/onefuzz/pull/702)
* Agent: Increased setup script timestamp resolution. [#709](https://github.com/microsoft/onefuzz/pull/709)
* Agent: Continued development related to an upcoming feature. [#508](https://github.com/microsoft/onefuzz/pull/508), [#688](https://github.com/microsoft/onefuzz/pull/688), [#703](https://github.com/microsoft/onefuzz/pull/703), [#710](https://github.com/microsoft/onefuzz/pull/710), [#711](https://github.com/microsoft/onefuzz/pull/711)

### Fixed
* Agent: Fixed support for libFuzzer targets that use shared objects or DLLs from the setup container. [#680](https://github.com/microsoft/onefuzz/pull/680), [#681](https://github.com/microsoft/onefuzz/pull/681), [#682](https://github.com/microsoft/onefuzz/pull/682), [#689](https://github.com/microsoft/onefuzz/pull/689), [#713](https://github.com/microsoft/onefuzz/pull/713)

## 2.9.0
### Added
* Contrib: Added sample Webhook Service [#666](https://github.com/microsoft/onefuzz/pull/666)
* Agent: Add OneFuzz version and Software role to telemetry [#586](https://github.com/microsoft/onefuzz/pull/586)
* Agent: Add multiple telemetry data types for the upcoming functionality [#619](https://github.com/microsoft/onefuzz/pull/619)
* Agent: Added `input_file_sha256` to [configuration value expansion](docs/command-replacements.md). [#641](https://github.com/microsoft/onefuzz/pull/641)
* Agent: Added `job_id` to Task Heartbeat [#646](https://github.com/microsoft/onefuzz/pull/646)
* Service: Added task information to [job_stopped](https://github.com/microsoft/onefuzz/blob/main/docs/webhook_events.md#job_stopped) events [#648](https://github.com/microsoft/onefuzz/pull/648)
 
### Changed
* Service: [task_stopped](https://github.com/microsoft/onefuzz/blob/main/docs/webhook_events.md#task_stopped) and [task_failed](https://github.com/microsoft/onefuzz/blob/main/docs/webhook_events.md#task_failed) now trigger once the task has stopped instead of upon entering the `stopping` state. [#651](https://github.com/microsoft/onefuzz/pull/651)
* CLI: Authentication tokens are saved upon successful login rather than on program exit. [#665](https://github.com/microsoft/onefuzz/pull/665)
* Service: If a task with dependent tasks fails, all of the dependent tasks are marked as failed.  [#650](https://github.com/microsoft/onefuzz/pull/650)
* Agent: Fixed PC address in crash report backtraces.  [#658](https://github.com/microsoft/onefuzz/pull/658)
* Service: Upon task completion, if all of the tasks in the associated job are completed, the job is marked as stopped.  [#649](https://github.com/microsoft/onefuzz/pull/649)
* Deployment/Agent: Updated AFL++ to 3.11c.  [#675](https://github.com/microsoft/onefuzz/pull/675)
* Agent/Proxy/Supervisor: Changed web request retry logic to always retry any request that fails, regardless of why the request failed.  [#674](https://github.com/microsoft/onefuzz/pull/674)
* Agent: Downloading files from task queues will now automatically retry on failure.  [#676](https://github.com/microsoft/onefuzz/pull/676)
* Service: User information is now stripped from [Events](docs/webhook_events.md) before being logged to Application Insights.  [#661](https://github.com/microsoft/onefuzz/pull/661)
 
### Fixed
* Service: Handle exception related to manually deleted scalesets [#672](https://github.com/microsoft/onefuzz/pull/672)
* Agent: Fixed Rust lifetime issues exposed by an update to Rust regex library [#671](https://github.com/microsoft/onefuzz/pull/671)

## 2.8.0
### Added
* CLI: Added support for [Aarch64](docs/how-to/fuzzing-other-architectures-on-azure.md) libFuzzer targets using the [QEMU user space emulator](https://qemu.readthedocs.io/en/latest/user/main.html). [#600](https://github.com/microsoft/onefuzz/pull/600)
* Build: Added CodeQL pipeline. [#617](https://github.com/microsoft/onefuzz/pull/617)
* Service: Added node and task heartbeat [events](docs/webhook_events.md).  [#621](https://github.com/microsoft/onefuzz/pull/621)

### Changed
* Agent: Clarified batch-processing logs.  [#622](https://github.com/microsoft/onefuzz/pull/622)
* Agent/Proxy: Updated multiple rust dependencies. [#624](https://github.com/microsoft/onefuzz/pull/624)
* Service/CLI/Contrib: Updated multiple python dependencies.  [#607](https://github.com/microsoft/onefuzz/pull/607), [#608](https://github.com/microsoft/onefuzz/pull/608), [#610](https://github.com/microsoft/onefuzz/pull/610), [#611](https://github.com/microsoft/onefuzz/pull/611), [#612](https://github.com/microsoft/onefuzz/pull/612), [#625](https://github.com/microsoft/onefuzz/pull/625), [#626](https://github.com/microsoft/onefuzz/pull/626), [#630](https://github.com/microsoft/onefuzz/pull/630), [#640](https://github.com/microsoft/onefuzz/pull/640)
* Service: Update task configuration to verify `target_exe` is a canonicalized relative path. [#613](https://github.com/microsoft/onefuzz/pull/613)
* Deployment/Agent: Updated AFL++ to 3.10c.  [#609](https://github.com/microsoft/onefuzz/pull/609)
* Deployment: Clarify application password creation succeeded after earlier failures.  [#629](https://github.com/microsoft/onefuzz/pull/629)
* Service: VM passwords are no longer set on Linux VMs.  [#620](https://github.com/microsoft/onefuzz/pull/620)
* Service: Clarify source of task failures when notification integration marks a task as failed.  [#635](https://github.com/microsoft/onefuzz/pull/635)

### Fixed
* Agent/Proxy/Supervisor: Fixed web request retry logic when handling operating system level errors.  [#623](https://github.com/microsoft/onefuzz/pull/623)
* Service: Handle exceptions when creating scalesets fail due to Azure VM quota issues. [#614](https://github.com/microsoft/onefuzz/pull/614)

## 2.7.0
### Added
* CLI: Added `onefuzz containers files download_dir` to enable downloading the contents of a container.  [#598](https://github.com/microsoft/onefuzz/pull/598)
* Agent: Added `microsoft_telemetry_key` and `instance_telemetry_key` and expanded the availability `reports_dir` in [configuration value expansion](docs/command-replacements.md). [#561](https://github.com/microsoft/onefuzz/pull/561)
* Agent/Service: Added `job_id` to agent-based heartbeats. [#594](https://github.com/microsoft/onefuzz/pull/594)
* Agent/Proxy/Supervisor: Added additional context to errors during Storage Queue and service interactions to improve debugging.  [#601](https://github.com/microsoft/onefuzz/pull/601)

### Changed
* Agent/Proxy/Supervisor: Renamed the Application Insights token names used for telemetry to `microsoft_telemetry_key` and `instance_telemetry_key` and the function that gated telemetry sharing to `can_share_with_microsoft` to make the telemetry implementation easier to understand. [#587](https://github.com/microsoft/onefuzz/pull/587)
* Deployment: Updated multiple Python dependencies. [#596](https://github.com/microsoft/onefuzz/pull/596)
* Service: Updated multiple Python dependencies. Addresses potential security issue [CVE-2020-28493](https://cve.mitre.org/cgi-bin/cvename.cgi?name=CVE-2020-28493) [#595](https://github.com/microsoft/onefuzz/pull/595)
* Service: Don't let nodes run new tasks if they are part of a scaleset or pool that is scheduled to be shut down. [#583](https://github.com/microsoft/onefuzz/pull/583)

### Fixed
* Service: Fixed the queries used to identify nodes running outdated OneFuzz releases. [#597](https://github.com/microsoft/onefuzz/pull/597)
* Agent: Fixed an issue that would stop an agent or supervisor from performing work if an HTTPS request has failed in certain conditions. [#603](https://github.com/microsoft/onefuzz/pull/603)
* Agent: Fixed an issue that would stop a task if the task printed a significant amount of data to stdout or stderr.  [#588](https://github.com/microsoft/onefuzz/pull/588)
* Deployment: Address deployment failures relating to cross-region Azure Active Directory resource creation delays. [#585](https://github.com/microsoft/onefuzz/pull/585)

## 2.6.0
### Added
* Service: Jobs that do not start within 30 days are automatically stopped. [#565](https://github.com/microsoft/onefuzz/pull/565)

### Changed
* Service: Debug proxies now use ports 28000 through 32000. [#552](https://github.com/microsoft/onefuzz/pull/552)
* Service: [Events](docs/webhook_events.md) now include the instance name and unique identifier.  [#577](https://github.com/microsoft/onefuzz/pull/577)
* Service: All task related [Events](docs/webhook_events.md) now include the task configuration.  [#580](https://github.com/microsoft/onefuzz/pull/580)
* Service: Errors generated during report crash report notification due to invalid jobs or tasks now include the reason for the error.  [#576](https://github.com/microsoft/onefuzz/pull/576)
* CLI: Namespaced containers for coverage used in job templates now include `build` and `platform` in addition to `project` and `name`. [#572](https://github.com/microsoft/onefuzz/pull/572)
* Service: User triggered node reimaging no longer waits for confirmation from the node prior to starting the reimage process. [#566](https://github.com/microsoft/onefuzz/pull/566)

### Fixed
* Service: Fixed an error condition when users recreate a container immediately after deleting it. [#582](https://github.com/microsoft/onefuzz/pull/582)
* Service: Fixed an issue when one task on a node ended, the node was reimaged regardless of the state of other tasks running on the node. [#567](https://github.com/microsoft/onefuzz/pull/567)

## 2.5.0
### Added
* CLI: Added the ability to poll task status until the tasks have started to managed templates using `--wait_for_running`.  [#532](https://github.com/microsoft/onefuzz/pull/532)
* CLI: Added a [libfuzzer-dotnet](docs/how-to/fuzzing-dotnet-with-libfuzzer.md) support.  [#535](https://github.com/microsoft/onefuzz/pull/535)
* Agent: Added `crashes_account` and `crashes_container` to [configuration value expansion](docs/command-replacements.md). [#551](https://github.com/microsoft/onefuzz/pull/551)
* CLI: Added `onefuzz status job` and `onefuzz status project` to provide a user-friendly job status.  [#550](https://github.com/microsoft/onefuzz/pull/550)

### Changed
* Agent: Logs and local telemetry from the agent now include the role (`agent` or `supervisor`) in recorded events.  [#527](https://github.com/microsoft/onefuzz/pull/527)
* Agent: Clarified the errors generated when libFuzzer coverage extraction fails [#554](https://github.com/microsoft/onefuzz/pull/554)

### Fixed
* Service: Handled `SkuNotAvailable` errors from Azure when creating scalesets. [#557](https://github.com/microsoft/onefuzz/pull/557)
* Agent/Proxy: Updated multiple third-party Rust libraries.  Addresses potential security issue [RUSTSEC-2021-0023](https://rustsec.org/advisories/RUSTSEC-2021-0023).  [#548](https://github.com/microsoft/onefuzz/pull/548)

## 2.4.1
### Changed
* Agent: Verifying LibFuzzer targets at the start of a task using `-help=1` now happens prior to sending heartbeats.  [#528](https://github.com/microsoft/onefuzz/pull/528)

### Fixed
* Service: Fixed issue related to Azure Functions not always providing the JWT token via Authorization headers. [#531](https://github.com/microsoft/onefuzz/pull/531)
* CLI: Fixed `--wait_for_running` in job templates. [#530](https://github.com/microsoft/onefuzz/pull/530)
* Deployment: Fixed a log error by setting the default SignalR transport used by Azure Functions. [#525](https://github.com/microsoft/onefuzz/pull/525)
* Agent: Fixed LibFuzzer coverage collection when instrumenting DLLs loaded at runtime. [#519](https://github.com/microsoft/onefuzz/pull/519)
* Service: Fixed issue where the cached Azure Identity was not being used. [#526](https://github.com/microsoft/onefuzz/pull/526)
* Service: Fixed log message related to identifying secondary corpus instances. [#524](https://github.com/microsoft/onefuzz/pull/524)

## 2.4.0
### Added
* Service: Handle scaleset nodes that never register, such as nodes with instance-specific setup script failures.  [#518](https://github.com/microsoft/onefuzz/pull/518)

### Changed
* Agent: Added stdout/stderr logging and clarifying context during failures to the `generic_analysis` task.  [#522](https://github.com/microsoft/onefuzz/pull/522)
* Agent/Service/Proxy: Clarify log messages from the scaleset proxy.  [#520](https://github.com/microsoft/onefuzz/pull/520)
* Agent/Proxy: Update multiple third-party Rust libraries.  [#517](https://github.com/microsoft/onefuzz/pull/517)

### Fixed
* Agent: Fixed potential race condition when single stepping when debugging during the `generic_crash_reporter` and `generic_generator` tasks running on Windows.  [#440](https://github.com/microsoft/onefuzz/pull/440)

## 2.3.0
### Changed
* Service: Clarify log messages when the service and agent versions mismatch. [#510](https://github.com/microsoft/onefuzz/pull/510)
* Service: Scalesets and Nodes are now updated in a consistent order during scheduled updates. [#512](https://github.com/microsoft/onefuzz/pull/512)
* CLI/Service: Expanded the use of Primitive data types that provide data validation. [#514](https://github.com/microsoft/onefuzz/pull/514)

### Fixed
* Service: Fixed an error generated when scalesets scheduled for deletion had configurations updated.  [#511](https://github.com/microsoft/onefuzz/pull/511)
* Service: Fixed an issue where scaleset configurations were updated too frequently.  [#511](https://github.com/microsoft/onefuzz/pull/511)

## 2.2.0
### Added
* Proxy: The logs from the proxy manager logged to Application Insights.  [#502](https://github.com/microsoft/onefuzz/pull/502)

### Changed
* Agent: Updated the web request retry logic to retry requests upon connection refused errors.  [#506](https://github.com/microsoft/onefuzz/pull/506)
* Service: Improved the performance of shutting down pools.  [#503](https://github.com/microsoft/onefuzz/pull/503)
* Service: Updated `azure-mgmt-compute` Python dependency. [#499](https://github.com/microsoft/onefuzz/pull/499)

### Fixed
* Proxy: Fixed an issue in the proxy heartbeats that caused proxy VMs to be reset after 10 minutes.  [#502](https://github.com/microsoft/onefuzz/pull/502)
* Agent: Fixed an issue that broke libFuzzer based crash reporting that was introduced 2.1.1. [#505](https://github.com/microsoft/onefuzz/pull/505)

## 2.1.1
### Added
* Agent: Added [Rust Clippy](https://github.com/rust-lang/rust-clippy) static analysis to CICD. [#490](https://github.com/microsoft/onefuzz/pull/490)
* CLI/Service: Added [Bandit](https://github.com/PyCQA/bandit) static analysis to CICD.  [#491](https://github.com/microsoft/onefuzz/pull/491)

### Fixed
* Service: Fixed an issue where scalesets could get in a state that would stop updating configurations.  [#489](https://github.com/microsoft/onefuzz/pull/489)

## 2.1.0
### Added
* Agent: Added `job_id` and `task_id` to [configuration value expansion](docs/command-replacements.md). [#481](https://github.com/microsoft/onefuzz/pull/481)
* Agent: Broadened the availability of `tools_dir` to [configuration value expansion](docs/command-replacements.md). [#480](https://github.com/microsoft/onefuzz/pull/480)
* Agent: Added clarifying context to command errors.  [#466](https://github.com/microsoft/onefuzz/pull/466)

### Changed
* CLI/Service/Agent: Supervisor can now be fully self-contained fuzzing tasks, no longer requiring `target_exe`.  Additionally, supervisor tasks can now optionally have managed report containers.  [#474](https://github.com/microsoft/onefuzz/pull/474)
* Service: Managed nodes that are unused beyond 7 days are automatically reimaged to ensure OS patch levels are maintained.  [#476](https://github.com/microsoft/onefuzz/pull/476)
* CLI/Service: Updated the default Windows VM image to `MicrosoftWindowsDesktop:Windows-10:20h2-pro:latest`.  Existing scalesets will not be impacted by this change, only newly created scalesets using the default image.  [#469](https://github.com/microsoft/onefuzz/pull/469)

### Fixed
* Agent: New inputs discovered by supervisor tasks are now saved to the `inputs` container.  [#484](https://github.com/microsoft/onefuzz/pull/484)
* CLI: The license is now properly set in the python package metadata.  [#472](https://github.com/microsoft/onefuzz/pull/472)
* Agent: Failure to download files via HTTP from queues now results in a failure, rather than the HTTP error being interpreted as the requested file.  [#485](https://github.com/microsoft/onefuzz/pull/485)
* Deployment: Fixed error when checking if the default CLI application exists.  [#488](https://github.com/microsoft/onefuzz/pull/488)

## 2.0.0
### Added
* Agent: Added clarifying context to file system errors.  [#423](https://github.com/microsoft/onefuzz/pull/423)
* CLI/Service: Significantly expanded the [events](docs/webhook_events.md) available for webhooks.  [#394](https://github.com/microsoft/onefuzz/pull/394)
* Agent: Added `{setup_dir}` to [configuration value expansion](docs/command-replacements.md) [#417](https://github.com/microsoft/onefuzz/pull/417)
* Agent: Added `{tools_dir}` [configuration value expansion](docs/command-replacements.md) to `{supervisor_options}` and `{supervisor_env}` [#444](https://github.com/microsoft/onefuzz/pull/444)

### Changed
* CLI/Service: Migrated `onefuzz status top` to use [Webhook Events](docs/webhook_events.md).  (BREAKING CHANGE) [#394](https://github.com/microsoft/onefuzz/pull/394)
* CLI/Service: New notification secrets, such as ADO tokens, are managed in Azure KeyVault and are no longer accessible to the user once created.  (BREAKING CHANGE) [#326](https://github.com/microsoft/onefuzz/pull/326), [#389](https://github.com/microsoft/onefuzz/pull/389)
* CLI/Service: Updated multiple Python dependencies. [#426](https://github.com/microsoft/onefuzz/pull/426), [#427](https://github.com/microsoft/onefuzz/pull/427),  [#430](https://github.com/microsoft/onefuzz/pull/430)

### Fixed
* Agent: Fixed triggering condition for new unique report events [#422](https://github.com/microsoft/onefuzz/pull/422)
* Deployment: Mitigate issues related to deployments within conditional access policy scenarios. [#447](https://github.com/microsoft/onefuzz/pull/447)
* Agent: Fixed an issue where unused nodes would stop requesting new work. [#459](https://github.com/microsoft/onefuzz/pull/459)
* Service: Fixed dead node cleanup. [#458](https://github.com/microsoft/onefuzz/pull/458)
* Service: Fixed an issue logging excessively large stdout/stderr from tasks.  [#460](https://github.com/microsoft/onefuzz/pull/460)

## 1.11.0
### Added 
* Service: Added support for sharding corpus storage accounts using "Premium" storage accounts for improved IOPs.  [#334](https://github.com/microsoft/onefuzz/pull/334)
* CLI/Service/Agent: Added the ability to optionally colocate multiple compatible tasks on a single machine. The coverage and crash reporting tasks in the LibFuzzer template make use of this functionality by default. [#402](https://github.com/microsoft/onefuzz/pull/402)
* CLI: Added `onefuzz debug log tail` which enables continuously following Application Insights query results.  [#401](https://github.com/microsoft/onefuzz/pull/401)
* CLI/Agent: Support verifying LibFuzzer targets at the start of a task using `-help=1`, which will enable identifying non-functional LibFuzzer targets.  [#381](https://github.com/microsoft/onefuzz/pull/381)
* CLI/Agent: Support specifying whether to log a warning or fail the task when a LibFuzzer target exits with a non-zero status code (without also generating a crashing input).  [#381](https://github.com/microsoft/onefuzz/pull/381)
* Agent: The stdout and stderr for the supervisors and generators are now logged to Application Insights.  [#400](https://github.com/microsoft/onefuzz/pull/400)
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
* Agent: Address issues handling LibFuzzer targets that produce non-UTF8 output to stderr.  [#379](https://github.com/microsoft/onefuzz/pull/379)

## 1.10.0
### Added
* CLI: Added `libfuzzer merge` job template, which enables running performing libFuzzer input minimization as a batch operation.  [#282](https://github.com/microsoft/onefuzz/pull/282)
* CLI/Service: Added the instance-specific Application Insights telemetry key to `onefuzz info get`, which will enable logging to the instance specific application insights from the SDK.  [#353](https://github.com/microsoft/onefuzz/pull/353)
* Agent: Added support for parsing ASAN `CHECK failed` entries, which can occur during large amounts of memory corruption.  [#358](https://github.com/microsoft/onefuzz/pull/358)
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
* Service: Fixed setting `target_workers`, used to configure the number of concurrent libFuzzer workers within a task. [#305](https://github.com/microsoft/onefuzz/pull/305)

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
* Agent: Added connection resiliency via automatic retry (with back-off) throughout the agent [#153](https://github.com/microsoft/onefuzz/pull/153)
* Deployment: Added the ability to log the application passwords during registration [#214](https://github.com/microsoft/onefuzz/pull/214)
* Agent: LibFuzzer Coverage metrics are now reported after the batch processing phase [#218](https://github.com/microsoft/onefuzz/pull/218)
* Deployment: Added a utility to assign scalesets to roles [#185](https://github.com/microsoft/onefuzz/pull/185)
* Contrib: Added a utility to automate deployment of new releases of OneFuzz via Azure Devops pipelines [#208](https://github.com/microsoft/onefuzz/pull/208)

### Fixed
* Agent: Addressed a race condition syncing input seeds [#204](https://github.com/microsoft/onefuzz/pull/204)

### Changed
* Agent: Instead of ignoring all access violations during libFuzzer coverage processing, stop on second-chance access violations [#210](https://github.com/microsoft/onefuzz/pull/210)
* Agent: During libFuzzer coverage, disable default symbol paths unless `_NT_SYMBOL_PATH` is set via `target_env`.  [#222](https://github.com/microsoft/onefuzz/pull/222)

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
* Service: Task error messages now limit the stdout and stderr to the last 4096 bytes [#170](https://github.com/microsoft/onefuzz/pull/170)
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
* Testing: Added rust based libFuzzer in the end-to-end integration tests [#132](https://github.com/microsoft/onefuzz/pull/132)

### Fixed
* Agent: Always parse stderr when generating crash reports for LibFuzzer instead of using `ASAN_OPTIONS=log_path`, which fixes crash reports from non-sanitizer based crashes. [#131](https://github.com/microsoft/onefuzz/pull/131)
* Deployment: Added data-migration script to fix notifications for pre-release installs [#135](https://github.com/microsoft/onefuzz/pull/135)

## 1.3.0
### Added
* Agent: Crash reports for LibFuzzer now attempts to parse stderr in addition to `ASAN_OPTIONS=log_path`.  This enables crash reporting of go-fuzz based binaries.  [#127](https://github.com/microsoft/onefuzz/pull/127)
* Deployment: During deployment, App Insights logs can be configured to automatically export logs to the `app-insights` container in instance specific `func` storage account.  [#102](https://github.com/microsoft/onefuzz/pull/102)

### Changed
* Agent: Reduced logs sent from the agent [#125](https://github.com/microsoft/onefuzz/pull/125)
* Service: Scalesets now use [multiple placement groups](https://docs.microsoft.com/en-us/azure/virtual-machine-scale-sets/virtual-machine-scale-sets-placement-groups#placement-groups), allowing a scaleset to grow to 1000 nodes (or 600 if using a custom image).  [#121](https://github.com/microsoft/onefuzz/pull/121)

### Fixed
* Deployment: Support deploying additional platforms (such as OSX).  [#126](https://github.com/microsoft/onefuzz/pull/126)
* Service: Fixed typing error in sorting TaskEvent.  [#129](https://github.com/microsoft/onefuzz/pull/129)

## 1.2.0
### Added
* CLI/Service: Added creating and updating [GitHub Issues](docs/notifications/github.md) based on crash reports.  [#110](https://github.com/microsoft/onefuzz/pull/110)

### Changed
* Agent: LibFuzzer fuzzing that exits with a non-zero exit code without a resulting crashing input now mark the task as failed.  [#108](https://github.com/microsoft/onefuzz/pull/108)
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
* Agent/Service: Refactored state management for on-VM supervisors [#96](https://github.com/microsoft/onefuzz/pull/96)
* Agent: Added 'done' semaphore to the agent to prevent agent from fetching additional work once the node should be reset.  [#86](https://github.com/microsoft/onefuzz/pull/86)
* Agent: Nodes now sleep longer between checking for new work.  [#78](https://github.com/microsoft/onefuzz/pull/78)
* Agent: The task execution clock is now started once the task is in the 'setting up' state [#82](https://github.com/microsoft/onefuzz/pull/82)
* Service: Drastically reduced logs sent to App Insights from third-party libraries [#63](https://github.com/microsoft/onefuzz/pull/63)
* Agent/Service: Added the ability to upgrade out-of-date VMs upon requesting new tasking [#35](https://github.com/microsoft/onefuzz/pull/35)
* CICD: Non-release builds now include the GIT hash in the versions and `localchanges` if built locally with un-committed code.  [#58](https://github.com/microsoft/onefuzz/pull/58)
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
* Agent: Supervisor now flushes logs to Application Insights upon exit [#21](https://github.com/microsoft/onefuzz/pull/21)
* Agent: Task specific setup script failures now properly get recorded as a failed task and trigger the node to be re-imaged [#24](https://github.com/microsoft/onefuzz/pull/24)


## 1.0.0
### Added
* Initial public release
