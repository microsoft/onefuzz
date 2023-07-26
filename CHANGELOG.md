<!-- markdownlint-disable-file MD024 -->

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 8.6.0

### Added

* Agent: Implemented `debuginfo` caching [#3280](https://github.com/microsoft/onefuzz/pull/3280)

### Changed

* Agent: Limit azcopy copy buffer to 512MB of RAM as the default maximum [#3293](https://github.com/microsoft/onefuzz/pull/3293)
* Agent: Define local fuzzing tasks relationships through new templating model [#3117](https://github.com/microsoft/onefuzz/pull/3117)
* Deployment: Replaced `--upgrade` flag with `--skip_aad_setup` flag in the deploy.py setup script [#3345](https://github.com/microsoft/onefuzz/pull/3345)
* Service: Make `ServiceConfiguration` eagerly evaluated [#3136](https://github.com/microsoft/onefuzz/pull/3136)
* Service: Improved `TimerRetention` performance through several UPN changes & fixes [#3289](https://github.com/microsoft/onefuzz/pull/3289)

### Fixed

* Agent: Fixed resolution of sibling .NET DLLs [#3325](https://github.com/microsoft/onefuzz/pull/3325)
* Agent/Service: Bumped several C# and Rust dependencies [#3319](https://github.com/microsoft/onefuzz/pull/3319), [#3320](https://github.com/microsoft/onefuzz/pull/3320), [#3317](https://github.com/microsoft/onefuzz/pull/3317), [#3297](https://github.com/microsoft/onefuzz/pull/3297), [#3301](https://github.com/microsoft/onefuzz/pull/3301), [#3291](https://github.com/microsoft/onefuzz/pull/3291), [#3195](https://github.com/microsoft/onefuzz/pull/3195), [#3328](https://github.com/microsoft/onefuzz/pull/3328)
* CLI: Look for azcopy.exe in environment variable `AZCOPY` and determine if it's actually referencing a directory [#3344](https://github.com/microsoft/onefuzz/pull/3344)
* CLI: Updated `repro get_files` to handle regression reports [#3340](https://github.com/microsoft/onefuzz/pull/3340)
* CLI: Fixed missing `target_timeout` setting in the Libfuzzer basic template [#3334](https://github.com/microsoft/onefuzz/pull/3334)
* CLI: Fixed false 'missing' dependency warning [#3331](https://github.com/microsoft/onefuzz/pull/3331)
* CLI: Fixed the `debug notification test_template` command expecting a `task_id`  [#3308](https://github.com/microsoft/onefuzz/pull/3308)
* Deployment: Update App Registration redirect URIs if deployment uses a custom domain [#3341](https://github.com/microsoft/onefuzz/pull/3341)
* Service: Fixed links in bugs filed from regression reports by populating `InputBlob` when possible [#3342](https://github.com/microsoft/onefuzz/pull/3342)
* Service: Fixed several storage issues to improve platform performance and reduce spurious `404`s [#3313](https://github.com/microsoft/onefuzz/pull/3313)
* Service: Added extra logging when `System.Title` is too long [#3332](https://github.com/microsoft/onefuzz/pull/3332)
* Service: Render `System.Title` before trying to trim it to the max allowed size [#3329](https://github.com/microsoft/onefuzz/pull/3329)
* Service: Differentiate `INVALID_JOB` and `INVALID_TASK` error codes [#3318](https://github.com/microsoft/onefuzz/pull/3318)

## 8.5.0

### Added

* Agent: Added tool to check source allowlists [#3246](https://github.com/microsoft/onefuzz/pull/3246)
* Agent: Precache `debuginfo` analysis for target exe in coverage example [#3225](https://github.com/microsoft/onefuzz/pull/3225)
* Agent/CLI/Service: Allow tasks environment variables to be set [#3294](https://github.com/microsoft/onefuzz/pull/3294)
* CLI/Service: Correlate cli to service to facilitate event lookups in AppInsights [#3137](https://github.com/microsoft/onefuzz/pull/3137)
* CLI: Added `--target_timeout` flag for qemu_user template command [#3277](https://github.com/microsoft/onefuzz/pull/3277)
* Documentation: Updated Threat Model [#3215](https://github.com/microsoft/onefuzz/pull/3215)
* Service: Added optional `Unless` condition when updating/re-opening Work Items [#3227](https://github.com/microsoft/onefuzz/pull/3227)
* Service: Include the task ID in the prerequisite task failure message [#3219](https://github.com/microsoft/onefuzz/pull/3219)
* Service: Added events retention policy  passed-integration-tests [#3186](https://github.com/microsoft/onefuzz/pull/3186)

### Changed

* Agent: Shrink published Rust debug info [#3247](https://github.com/microsoft/onefuzz/pull/3247), [#3252](https://github.com/microsoft/onefuzz/pull/3252)
* Agent: Get rid of yanked hermit-abi versions [#3270](https://github.com/microsoft/onefuzz/pull/3270)
* Documentation: Updated coverage docs to use correct quotes [#3279](https://github.com/microsoft/onefuzz/pull/3279)
* Service: Better errors from Download: Make `GetFileSasUrl` nullable [#3229](https://github.com/microsoft/onefuzz/pull/3229)
* Service: Changed template rendering from async to synchronous [#3241](https://github.com/microsoft/onefuzz/pull/3241)
* Service: Log webhook exception as an "error" since we are retrying anyways [#3238](https://github.com/microsoft/onefuzz/pull/3238)
* Service: Make `WebhookMessageEventGrid` compatible with the event grid format [#3286](https://github.com/microsoft/onefuzz/pull/3286)

### Fixed

* Agent: Improved .dll redirection by setting up .local file before invoking LibFuzzer [#3269](https://github.com/microsoft/onefuzz/pull/3269)
* Agent/Service: Bumped several C#, Rust dependencies, and Rust version to 1.71 [#3278](https://github.com/microsoft/onefuzz/pull/3278), [#3281](https://github.com/microsoft/onefuzz/pull/3281), [#3221](https://github.com/microsoft/onefuzz/pull/3221), [#3230](https://github.com/microsoft/onefuzz/pull/3230), [#3231](https://github.com/microsoft/onefuzz/pull/3231), [#3203](https://github.com/microsoft/onefuzz/pull/3203), [#3240](https://github.com/microsoft/onefuzz/pull/3240), [#3239](https://github.com/microsoft/onefuzz/pull/3239), [#3199](https://github.com/microsoft/onefuzz/pull/3199), [#3254](https://github.com/microsoft/onefuzz/pull/3254), [#3257](https://github.com/microsoft/onefuzz/pull/3257), [#3273](https://github.com/microsoft/onefuzz/pull/3273), [#3258](https://github.com/microsoft/onefuzz/pull/3258), [#3271](https://github.com/microsoft/onefuzz/pull/3271), [#3292](https://github.com/microsoft/onefuzz/pull/3292)
* CLI/Service: Fixed regression bugs, file bugs on `regression_report` and properly reset state on duplicates [#3263](https://github.com/microsoft/onefuzz/pull/3263)
* Service: Improve Azure DevOps validation problem reporting and resiliency [#3222](https://github.com/microsoft/onefuzz/pull/3222)
* Service: Updated KeyVault access policy for Azure WebSites service account access [#3109](https://github.com/microsoft/onefuzz/pull/3109)
* Service: Switched to default `HttpCompletion`, which is `ResponseRead` to attempt to prevent webhooks occasionally failing to send [#3259](https://github.com/microsoft/onefuzz/pull/3259)
* Service: Fixed `Timestamp` response from API [#3237](https://github.com/microsoft/onefuzz/pull/3237)
* Service: Trim `System.Title` if length is longer than 128 characters [#3284](https://github.com/microsoft/onefuzz/pull/3284)

## 8.4.0

### Added

* Agent: Include debug info in the release binaries to improve backtraces and debuggability [#3194](https://github.com/microsoft/onefuzz/pull/3194)
* Agent: Added a timeout when closing the app insight channels [#3181](https://github.com/microsoft/onefuzz/pull/3181)
* Agent: Require input marker in arguments when given an input corpus directory [#3205](https://github.com/microsoft/onefuzz/pull/3205)
* Agent/CLI/Service: Added `extra_output` container, rename `extra` container [#3064](https://github.com/microsoft/onefuzz/pull/3064)
* Agent: Creating `CustomMetrics` for Rust `CustomEvents` [#3188](https://github.com/microsoft/onefuzz/pull/3188)
* Agent: Added prereqs for implementing caching for coverage locations and debuginfo in `coverage` task [#3218](https://github.com/microsoft/onefuzz/pull/3218)
* CLI: Added command `onefuzz repro get_files` for downloading files to locally reproduce a crash [#3160](https://github.com/microsoft/onefuzz/pull/3160)
* CLI: Added command `onefuzz debug notification test_template <template> [--task_id <task_id>] [--report <report>]` to allow a report to be sent when debugging [#3206](https://github.com/microsoft/onefuzz/pull/3206)
* Documentation: Added documentation on how to use the validation tools [#3212](https://github.com/microsoft/onefuzz/pull/3212)

### Changed

* Agent: Removed agent traces from AppInsights [#3143](https://github.com/microsoft/onefuzz/pull/3143)
* Agent: Include debug info in the release binaries to improve backtraces and debuggability [#3194](https://github.com/microsoft/onefuzz/pull/3194)
* Agent: Make coverage-recording errors non-fatal [#3166](https://github.com/microsoft/onefuzz/pull/3166)
* Deployment/Service: Enable custom metrics app config value [#3190](https://github.com/microsoft/onefuzz/pull/3190)
* Documentation: Renamed example `coverage.rs` to `record.rs` to match documentation [#3204](https://github.com/microsoft/onefuzz/pull/3204)
* Service: Moved authentication into middleware [#3133](https://github.com/microsoft/onefuzz/pull/3133)
* Service: Store authentication information in KeyVault [#3127](https://github.com/microsoft/onefuzz/pull/3127), [#3223](https://github.com/microsoft/onefuzz/pull/3223)
* Service: Port current logging implementation to ILogger [#3173](https://github.com/microsoft/onefuzz/pull/3173)
* Service: Added improved error reporting from scale-in protection modification [#3184](https://github.com/microsoft/onefuzz/pull/3184)
* Service: Downgraded queue error to warning when retrying because the message is too large [#3224](https://github.com/microsoft/onefuzz/pull/3224)

### Fixed

* Agent: Skip entire function if entry offset excluded [#3172](https://github.com/microsoft/onefuzz/pull/3172)
* Agent: Try to kill debuggee if Linux recording times out [#3177](https://github.com/microsoft/onefuzz/pull/3177)
* Agent: Apply allowlist to source conversion in coverage task [#3208](https://github.com/microsoft/onefuzz/pull/3208)
* Service: Bumped C# and Rust dependencies [#3200](https://github.com/microsoft/onefuzz/pull/3200), [#3165](https://github.com/microsoft/onefuzz/pull/3165), [#3168](https://github.com/microsoft/onefuzz/pull/3168), [#3153](https://github.com/microsoft/onefuzz/pull/3153), [#3169](https://github.com/microsoft/onefuzz/pull/3169), [#3185](https://github.com/microsoft/onefuzz/pull/3185), [#3191](https://github.com/microsoft/onefuzz/pull/3191), [#3163](https://github.com/microsoft/onefuzz/pull/3163), [#3209](https://github.com/microsoft/onefuzz/pull/3209), [#3146](https://github.com/microsoft/onefuzz/pull/3146), [#3198](https://github.com/microsoft/onefuzz/pull/3198)

## 8.3.0

### Changed

* CLI/Service: Don’t validate error codes on client side [#3131](https://github.com/microsoft/onefuzz/pull/3131)

### Fixed

* Agent: Switched from unmaintained Rust dependency `tui` to `ratatui` [#3155](https://github.com/microsoft/onefuzz/pull/3155)
* Agent: Removed dependency on the abandoned Rust `users` crate [#3150](https://github.com/microsoft/onefuzz/pull/3150)
* Agent/CLI/Service: Bumped several C#, Python, and Rust dependencies [#3118](https://github.com/microsoft/onefuzz/pull/3118), [#3132](https://github.com/microsoft/onefuzz/pull/3132), [#3088](https://github.com/microsoft/onefuzz/pull/3088), [#3106](https://github.com/microsoft/onefuzz/pull/3106), [#3140](https://github.com/microsoft/onefuzz/pull/3140), [#3120](https://github.com/microsoft/onefuzz/pull/3120), [#3145](https://github.com/microsoft/onefuzz/pull/3145), [#3151](https://github.com/microsoft/onefuzz/pull/3151)
* CLI/Service: Include a reason when a task has never started [#3148](https://github.com/microsoft/onefuzz/pull/3148)
* Service: Fixed bug for scale-in protection [#3144](https://github.com/microsoft/onefuzz/pull/3144)

## 8.2.0

### Added

* Service: Created `CustomMetrics` for the Node and Task Heartbeat. [#3082](https://github.com/microsoft/onefuzz/pull/3082)
* Service: Add an event for Repro VM creation. [#3091](https://github.com/microsoft/onefuzz/pull/3091)
* Service: Add more context to the deletion of nodes. [#3102](https://github.com/microsoft/onefuzz/pull/3102)
* Documentation: Create documentation for events 2.0 migration. [#3098](https://github.com/microsoft/onefuzz/pull/3098)

### Changed

* Agent: Match the agent version to the server [#3093](https://github.com/microsoft/onefuzz/pull/3093)
* Service: Increase lock wait timeout for `qemu_user` setup script. [#3114](https://github.com/microsoft/onefuzz/pull/3114)

### Fixed

* Service: Fixed issue that incorrectly marked tasks as failed. [#3083](https://github.com/microsoft/onefuzz/pull/3083)
* Service: Fixed bug when truncating reports. [#3103](https://github.com/microsoft/onefuzz/pull/3103)
* Service: Allow use of `readonly_inputs` for `qemu_user` template. [#3116](https://github.com/microsoft/onefuzz/pull/3116)
* Service: Fix logic to set `check_fuzzer_help`. [#3130](https://github.com/microsoft/onefuzz/pull/3130)
* CLI: Fix CLI failure dude to ErrorCode enums out of sync. [#3129](https://github.com/microsoft/onefuzz/pull/3129)

## 8.1.0

### Added

* Agent: Added coverage percentage in Cobertura reports [#3034](https://github.com/microsoft/onefuzz/pull/3034)
* Agent: Added `maxPerPage` to ORM [#3016](https://github.com/microsoft/onefuzz/pull/3016)
* CLI: Added `onefuzz containers files download` command to download the blob content to a file [#3060](https://github.com/microsoft/onefuzz/pull/3060)

### Changed

* Agent: Reconfigured OneFuzz agent to not consume `S_LABEL` symbols from PDBs [#3046](https://github.com/microsoft/onefuzz/pull/3046)
* Agent: Update `elsa::sync::FrozenMap` now implements Default [#3044](https://github.com/microsoft/onefuzz/pull/3044)
* Agent: Updated agent to use insta Rust crate for snapshot tests of stacktrace parsing [#3027](https://github.com/microsoft/onefuzz/pull/3027)
* Agent/CLI/Deployment: Store event payloads as blobs. Add API to download event payload given event id. [#3069](https://github.com/microsoft/onefuzz/pull/3069)
* Agent/Service: Bumped Rust version, several Rust dependencies, and several C# dependencies [#3049](https://github.com/microsoft/onefuzz/pull/3049), [#3037](https://github.com/microsoft/onefuzz/pull/3037), [#3031](https://github.com/microsoft/onefuzz/pull/3031), [#3023](https://github.com/microsoft/onefuzz/pull/3023), [#2972](https://github.com/microsoft/onefuzz/pull/2972), [#2814](https://github.com/microsoft/onefuzz/pull/2814), [#3052](https://github.com/microsoft/onefuzz/pull/3052), [#3067](https://github.com/microsoft/onefuzz/pull/3067), [#3068](https://github.com/microsoft/onefuzz/pull/3068), [#3056](https://github.com/microsoft/onefuzz/pull/3056), [#2958](https://github.com/microsoft/onefuzz/pull/2958)
* Service: Made our validation errors more specific so that we can handle them appropriately and reference them in documentation [#3053](https://github.com/microsoft/onefuzz/pull/3053)
* Service/CLI: Updated the Azure DevOps logic to consume the list of existing items once [#3014](https://github.com/microsoft/onefuzz/pull/3014)
* Service: Cap recursion in ORM [#2992](https://github.com/microsoft/onefuzz/pull/2992)
* Service: Collect additional report field in an `ExtensionData` property [#3079](https://github.com/microsoft/onefuzz/pull/3079)

### Fixed

* Agent: Parse .NET exception stack traces when we see them in crash log outputs [#2988](https://github.com/microsoft/onefuzz/pull/2988)
* Agent: Tweaked some of the parameters for the agent's logging to avoid task logger occasionally skipping messages [#3070](https://github.com/microsoft/onefuzz/pull/3070)
* Agent: Allow libfuzzer verification to retry [#3032](https://github.com/microsoft/onefuzz/pull/3032)
* Agent: Fixed typo in AzCopy parameter name and set default value to true [#3085](https://github.com/microsoft/onefuzz/pull/3085)
* Agent/CLI: Added new endpoint to update the pool authentication in order to fix multiple stop messages from being sent after node shuts down [#3059](https://github.com/microsoft/onefuzz/pull/3059)
* CLI: Changed `--check_fuzzer_help` to `--no_check_fuzzer_help` [#3063](https://github.com/microsoft/onefuzz/pull/3063)
* Service: Include exception information when validation fails [#3077](https://github.com/microsoft/onefuzz/pull/3077)
* Service: Added another truncation case for 'Request body too large...' errors [#3075](https://github.com/microsoft/onefuzz/pull/3075)
* Service: Fixed the logic for marking task as failed [#3083](https://github.com/microsoft/onefuzz/pull/3083)
* Service: Fixed error deserializing events from the events container [#3089](https://github.com/microsoft/onefuzz/pull/3089)

## 8.0.0

## BREAKING CHANGES

This release removes the parameters `--client_id`, `--override_authority`, and `override_tenant_domain` from the `config` command.

For those accessing the CLI with a service principal, the parameters can be supplied on the command line for each of the CLI commands.

For example, if deploying a job:

```shell
onefuzz --client_id [CLIENT_ID] --client_secret [CLIENT_SECRET] template libfuzzer basic --setup_dir .....
```

### Added

* Agent: Added `validate` command to the agent to help validate a fuzzer [#2948](https://github.com/microsoft/onefuzz/pull/2948)
* CLI: Added option to libfuzzer template to specify a known crash container [#2950](https://github.com/microsoft/onefuzz/pull/2950)
* CLI: Added option to libfuzzer template to specify the duration of the tasks independently from the job duration [#2997](https://github.com/microsoft/onefuzz/pull/2997)

### Changed

* Agent: Install v17 Visual Studio redistributables [#2943](https://github.com/microsoft/onefuzz/pull/2943)
* Agent/Service: Use minimized stack for crash site if no ASAN logs are available [#2962](https://github.com/microsoft/onefuzz/pull/2962)
* Agent/Service: Unified several Rust crate dependency versions across the platform [#3010](https://github.com/microsoft/onefuzz/pull/3010)
* CLI: Remove additional parameters from the `config` command and require them on each CLI request if accessing the CLI with a service principal [#3000](https://github.com/microsoft/onefuzz/pull/3000)
* Service: Loosen scriban template validation [#2963](https://github.com/microsoft/onefuzz/pull/2963)
* Service: Updated integration test pool size [#2935](https://github.com/microsoft/onefuzz/pull/2935)
* Service: Pass the task tags to the agent when scheduling jobs [#2881](https://github.com/microsoft/onefuzz/pull/2881)

### Fixed

* Agent: Ensure custom `target_options` are always passed last to the fuzzer [#2952](https://github.com/microsoft/onefuzz/pull/2952)
* Agent: Removed xml-rs dependency [#2936](https://github.com/microsoft/onefuzz/pull/2936)
* Agent: Better logging of failures in the task_logger [#2940](https://github.com/microsoft/onefuzz/pull/2940)
* Agent/Service: Updates to address CVE's [#2931](https://github.com/microsoft/onefuzz/pull/2931), [#2957](https://github.com/microsoft/onefuzz/pull/2957), [#2967](https://github.com/microsoft/onefuzz/pull/2967)
* Deployment/Service: Renamed EventGrid subscription to conform with EventGrid's naming scheme [#2960](https://github.com/microsoft/onefuzz/pull/2960)
* Deployment/Service: Added required KeyVault access policy allowing OneFuzz Function App to use an SSL cert for custom domain endpoints [#3004](https://github.com/microsoft/onefuzz/pull/3004), [#3006](https://github.com/microsoft/onefuzz/pull/3006)
* Documentation: Updated 'Azure Devops Work Item creation' doc to remove an outdated template reference [#2956](https://github.com/microsoft/onefuzz/pull/2956)
* Service: Updated feature configuration package to fix an issue where 2 feature flags were using the same ID [#2980](https://github.com/microsoft/onefuzz/pull/2980)
* Service: Make `GetNotification` nullable to fix errors looking up non-existent notification IDs [#2981](https://github.com/microsoft/onefuzz/pull/2981)
* Service: UniqueReports should be UniqueInputs in LibFuzzer merge task [#2982](https://github.com/microsoft/onefuzz/pull/2982)
* Service: Fix Notification `delete` action [#2987](https://github.com/microsoft/onefuzz/pull/2987)
* Service: Added handle for missing unique field key in `AdoFields` [#2986](https://github.com/microsoft/onefuzz/pull/2986)
* Service: Implemented `ITruncatable` for `JobConfig` & `EventJobStopped` to avoid exceptions for messages being too large for Azure Queue [#2993](https://github.com/microsoft/onefuzz/pull/2993)

## 7.0.0

## BREAKING CHANGES

* This release has fully deprecated `jinja` templates and will only accept `scriban` templates.
* The `onefuzz config` command has removed the `--authority` and `--tenant_domain` parameters. The only _required_ parameter for interactive use is the `--endpoint` parameters. The other values needed for authentication are now retrieved dynamically.
* The recording components used in the `coverage` task have been rewritten for improved source-level reporting. The task-level API has one breaking change: the `coverage_filter` field has been removed and replaced by the `module_allowlist` and `source_allowlist` fields. See [here](https://github.com/microsoft/onefuzz/blob/5bfcc4e242aa041d8c067471ee2e81904589a79e/src/agent/coverage/README.md#allowlists) for documentation of the new format.
* The old `dotnet` template has been removed and `dotnet_dll` is now `dotnet`.

### Added

* Service: Added  unmanaged nodes integration tests. [#2780](https://github.com/microsoft/onefuzz/pull/2780)
* CLI: Added notification `get` command to retrieve specific notification definitions. [#2818](https://github.com/microsoft/onefuzz/pull/2818)
* Agent: Added function allow-list to the coverage example exe. [#2830](https://github.com/microsoft/onefuzz/pull/2830)
* Service: Added feature flag, validation when new notifications are created, and CLI support for migration to scriban. [#2816](https://github.com/microsoft/onefuzz/pull/2816), [#2834](https://github.com/microsoft/onefuzz/pull/2834), [#2839](https://github.com/microsoft/onefuzz/pull/2839)
* Agent: Switch over to new `coverage` task. [#2741](https://github.com/microsoft/onefuzz/pull/2741)
* Service: Added `--notification_config` support for dotnet templates. [#2842](https://github.com/microsoft/onefuzz/pull/2842)
* Service: Report extension errors when deploying VM in a scaleset. [#2846](https://github.com/microsoft/onefuzz/pull/2846)
* Service: Semantically validate notification configurations. [#2850](https://github.com/microsoft/onefuzz/pull/2850)
* Agent: Accept optional `dir` of coverage test inputs. [#2853](https://github.com/microsoft/onefuzz/pull/2853)
* Service/Agent: Added extra container to tasks. [#2847](https://github.com/microsoft/onefuzz/pull/2847)
* Documentation: Document `coverage` crate and tool. [#2904](https://github.com/microsoft/onefuzz/pull/2904)
* Agent: Add the ability for a task to gracefully shutdown when a task is stopped. [#2912](https://github.com/microsoft/onefuzz/pull/2912)

### Changed

* Service: Deprecated the job template feature. [#2798](https://github.com/microsoft/onefuzz/pull/2798)
* Service: Deploy with scriban only, removing jinja. [#2809](https://github.com/microsoft/onefuzz/pull/2809)
* Agent: Defer setting coverage breakpoints. This avoids breaking hot patching routines in the ASan interceptor
initializers. [#2832](https://github.com/microsoft/onefuzz/pull/2832)
* Service: Updated remaining jinja docs. [#2838](https://github.com/microsoft/onefuzz/pull/2838)
* Service: Support another exception case when adding `AssignedTo` to telemetry. [#2829](https://github.com/microsoft/onefuzz/pull/2829)
* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies.[#2849](https://github.com/microsoft/onefuzz/pull/2849), [#2855](https://github.com/microsoft/onefuzz/pull/2855), [#2274](https://github.com/microsoft/onefuzz/pull/2274), [#2544](https://github.com/microsoft/onefuzz/pull/2544), [#2857](https://github.com/microsoft/onefuzz/pull/2857), [#2876](https://github.com/microsoft/onefuzz/pull/2876)
* Contrib: Updated contribution `onefuzz config` command lines. [#2861](https://github.com/microsoft/onefuzz/pull/2861)
* Agent: Removed Z3 telemetry. [#2860](https://github.com/microsoft/onefuzz/pull/2860)
* Service: Change the optional parameter names and set an expiration for the cache created on the `onefuzz config` command. [#2835](https://github.com/microsoft/onefuzz/pull/2835)
* Agent: Removed the function allowlist. [#2859](https://github.com/microsoft/onefuzz/pull/2859)
* Agent: Updated clap to remove suppressions. [#2856](https://github.com/microsoft/onefuzz/pull/2856)
* Agent: Removed unused telemetry data. [#2863](https://github.com/microsoft/onefuzz/pull/2863)
* CLI: Removed old `libfuzzer dotnet` template. [#2875](https://github.com/microsoft/onefuzz/pull/2875)
* Test: Updated C# functional testing InfoResponse. [#2894](https://github.com/microsoft/onefuzz/pull/2894)
* Service: Updated the truncating logic when getting the error so that we retrieve the last messages. [#2896](https://github.com/microsoft/onefuzz/pull/2896)
* Service: Added additional filter check for reports and regressions. [#2911](https://github.com/microsoft/onefuzz/pull/2911)

### Fixed

* Agent: Removed a stray print statement. [#2823](https://github.com/microsoft/onefuzz/pull/2823)
* Deployment: Fixed a bug in `registration.py` when creating CLI service principals. [#2828](https://github.com/microsoft/onefuzz/pull/2828)
* Example: Fixed coverage example build. [#2831](https://github.com/microsoft/onefuzz/pull/2831)
* Service: Fixed the way we report an error when creating a Scaleset under a missing Pool. [#2844](https://github.com/microsoft/onefuzz/pull/2844)
* Service: Update SharpFuzz to a version that supports .NET7.0, and change .NET installation method. [#2878](https://github.com/microsoft/onefuzz/pull/2878)
* Deployment: Fixed an error where a variable was being referenced before being assigned. [#2903](https://github.com/microsoft/onefuzz/pull/2903)
* Service: Created a wrapper function to handle columns defined as GUID in tables. [#2898](https://github.com/microsoft/onefuzz/pull/2898)
* Service: Pass `PreserveExistingOutputs` to the task. [#2905](https://github.com/microsoft/onefuzz/pull/2905)
* Service: Fixed notification validation. [#2914](https://github.com/microsoft/onefuzz/pull/2914)
* Service: Fixed the custom script definition that could prevent the creation of the repro VM due to a change in the underlying extension setup processes. [#2920](https://github.com/microsoft/onefuzz/pull/2920)
* Deployment: Fixed `--auto_create_cli_app` flag bug used during deployment. [#2921](https://github.com/microsoft/onefuzz/pull/2921)
* Agent/Service: Updates to address CVE's. [#2933](https://github.com/microsoft/onefuzz/pull/2933)
* Service: Fixed a condition when generating a task configuration. [#2925](https://github.com/microsoft/onefuzz/pull/2925)

## 6.4.0

### Added

* Deployment/CLI: OneFuzz Config refactored - `tenant_id`, `tenant_domain`, `multi_tenant_domain`, and `cli_client_id` are now required values in the config.json used during deployment and no longer required when running the config command. [#2771](https://github.com/microsoft/onefuzz/pull/2771), [#2811](https://github.com/microsoft/onefuzz/pull/2811)
* Agent: Fully escape allowlist rules [#2784](https://github.com/microsoft/onefuzz/pull/2784)
* Agent: Apply allowlist to all blocks within a function [#2785](https://github.com/microsoft/onefuzz/pull/2785)
* CLI: Added a cli subcommand `onefuzz debug notification template` to validate scriban notification templates [#2800](https://github.com/microsoft/onefuzz/pull/2800)
* Service: Added Notification failure webhook to communicate Notification failures [#2628](https://github.com/microsoft/onefuzz/pull/2628)

### Changed

* Service: Include `AssignedTo` when failing to create a work item due to an authentication exception [#2770](https://github.com/microsoft/onefuzz/pull/2770)

### Fixed

* Agent: Fixes & improvements to `Expand` behavior [#2789](https://github.com/microsoft/onefuzz/pull/2789)
* Agent: Triming whitespace in output from monitored process before printing [#2782](https://github.com/microsoft/onefuzz/pull/2782)
* CLI: Fixed default value of analyzer_exe [#2797](https://github.com/microsoft/onefuzz/pull/2797)
* CLI: Fixed missing `readonly_inputs` parameter in dotnet & dotnet_dll templates [#2740](https://github.com/microsoft/onefuzz/pull/2740)
* Service: Fixed query to get the existing proxy [#2791](https://github.com/microsoft/onefuzz/pull/2791)
* Service: Truncate webhooks message length for azure queue size compatibility [#2788](https://github.com/microsoft/onefuzz/pull/2788)

## 6.3.0

### Added

* Service: Add Optional Analysis Task to Libfuzzer Template [#2748](https://github.com/microsoft/onefuzz/pull/2748)
* Agent: Use `elsa` for improved interface with `debuggable_module::Loader` [#2703](https://github.com/microsoft/onefuzz/pull/2703)
* Agent: Add sourceline output and logging to coverage example [#2753](https://github.com/microsoft/onefuzz/pull/2753)
* Agent: Fix Linux detection of shared library mappings [#2754](https://github.com/microsoft/onefuzz/pull/2754)
* Agent: Support AllowList extension [#2756](https://github.com/microsoft/onefuzz/pull/2756)
* Agent: Add `stdio` dumping to example [#2757](https://github.com/microsoft/onefuzz/pull/2757)

### Changed

* Service: Update Azure Cli [#2733](https://github.com/microsoft/onefuzz/pull/2733)
* Service: Truncate Large Webhook Events [#2742](https://github.com/microsoft/onefuzz/pull/2742)
* Service: Wrap fallible ORM functions in try/catch [#2745](https://github.com/microsoft/onefuzz/pull/2745)
* Agent/Supervisor/Proxy: Updated third-party Rust dependencies. [#2744](https://github.com/microsoft/onefuzz/pull/2744)

### Fixed

* Agent: Fixed Mulit-Agent Issue - Added `machine_id` to config_path and failure_path of the Agent [#2731](https://github.com/microsoft/onefuzz/pull/2731)
* Service: Fixed Proxy Table Query [#2743](https://github.com/microsoft/onefuzz/pull/2743)
* Service: Fix Notification Logic and Regression Reporting [#2751](https://github.com/microsoft/onefuzz/pull/2751)[#2758](https://github.com/microsoft/onefuzz/pull/2758)

## 6.2.0

### Added

* Agent: Added more into-JSON coverage conversions [#2725](https://github.com/microsoft/onefuzz/pull/2725)
* Agent: Added binary coverage merging measurements [#2724](https://github.com/microsoft/onefuzz/pull/2724)
* Agent: Added deserialization compatibility functions [#2719](https://github.com/microsoft/onefuzz/pull/2719)
* Agent: Added OS-generic `CoverageRecord` builder to capture output of target child process and allow `Loader` reuse in coverage recording [#2716](https://github.com/microsoft/onefuzz/pull/2716)
* Agent: Improve source coverage of HTML reports [#2700](https://github.com/microsoft/onefuzz/pull/2700), [#2701](https://github.com/microsoft/onefuzz/pull/2701), [#2706](https://github.com/microsoft/onefuzz/pull/2706)
* Deployment: Added support for custom domain names used as OneFuzz endpoints [#2720](https://github.com/microsoft/onefuzz/pull/2720)
* Service: Added documentation for unmanaged node deployment [#2694](https://github.com/microsoft/onefuzz/pull/2694)

### Changed

* Agent: Use a custom `Output` type when recording coverage [#2723](https://github.com/microsoft/onefuzz/pull/2723)
* Agent: Reduce mutation in the agent state machine [#2710](https://github.com/microsoft/onefuzz/pull/2710)
* Service: Include dotnet version in `info` response [#2693](https://github.com/microsoft/onefuzz/pull/2693)
* Service: Use feature flags to get the node disposal strategy [#2713](https://github.com/microsoft/onefuzz/pull/2713)

### Fixed

* Agent: Escape periods when converting globs [#2721](https://github.com/microsoft/onefuzz/pull/2721)
* Agent: Ignore benign recv hangup in agent timer functions [#2722](https://github.com/microsoft/onefuzz/pull/2722)
* Agent: Fix NullRef exception when getting a scaleset that does not exist [#2692](https://github.com/microsoft/onefuzz/pull/2692)
* Service: Downgrade error on _"cannot delete nodes from scaleset"_ to a warning [#2691](https://github.com/microsoft/onefuzz/pull/2691)
* Service: Fixed build issue related to dotnet version `7.0.101` [#2698](https://github.com/microsoft/onefuzz/pull/2698)
* Service: Adding `public` identifier to `Events` to restore missing events [#2705](https://github.com/microsoft/onefuzz/pull/2705)

## 6.1.0

### Added

* Service: Added support for feature flags which allows us to deploy new code in parts and turn it on when it's ready. [#2620](https://github.com/microsoft/onefuzz/pull/2620)
* Service: Added a validation endpoint for the notification template. [#2655](https://github.com/microsoft/onefuzz/pull/2655)

### Changed

* Service: Update LLVM from v10 to v12 now that we are supporting Ubuntu 20.04 as our default image. [#2617](https://github.com/microsoft/onefuzz/pull/2617)
* Agent: Remove unused coverage recorder from `input-tester`. [#2681](https://github.com/microsoft/onefuzz/pull/2681)
* Agent: Rename `coverage` to `coverage-legacy`. [#2685](https://github.com/microsoft/onefuzz/pull/2685)

### Fixed

* CLI: Return an error when uppercase application names are specified when using deploy.py. [#2665](https://github.com/microsoft/onefuzz/pull/2665)
* Agent: Fix local fuzzing mode. [#2669](https://github.com/microsoft/onefuzz/pull/2669)
* Service: Post the JobCreated event when a job is created. [#2677](https://github.com/microsoft/onefuzz/pull/2677)
* Service: The repro `Create` command will now fail if insert fails. Also add additional tests. [#2678](https://github.com/microsoft/onefuzz/pull/2678)
* Service: Added support for `Contains Words` in WIQL [#2686](https://github.com/microsoft/onefuzz/pull/2686)

## 6.0.0

## BREAKING CHANGES

### Manual Deployment Step

When upgrading from version 5.20 a manual step is required. Before deploying 6.0 delete both Azure App Functions and the Azure App Service plan before upgrading. This is required because we have migrated the service from `python` to `C#`.

After deployment, there will be two App Functions deployed, one with the name of the deployment and a second one with the same name and a `-net` suffix. This is a temporary situation and the `-net` app function will be removed in a following release.

If you have not used the deployment parameters to deploy C# functions in 5.20, you can manually delete the `-net` app function immediately. Deploying the C# functions was not a default action in 5.20, for most deployments deleting the `-net` app function immediately is ok.

### Deprecation of jinja templates

With this release we are moving from jinja templates to [scriban](https://github.com/scriban/scriban) templates. See the documentation for [scriban here](https://github.com/scriban/scriban/tree/master/doc).

Version 6.0 will convert jinja templates on-the-fly for a short period of time. We do **_not_** guarantee that this will be successful for all jinja template options. These on-the-fly conversions are not persisted in the notifications table in this release. They will be in a following release. This will allow time for conversions of templates that are not handled by the current automatic conversion process.

### CLI

The default value for the `--container_type` parameter to the `container` command has been removed. The `container_type` parameter is still required for the command. This change removes the ambiguity of the container information being returned.

### Added

* Agent: Added `machine_id` a parameter of the agent config. [#2649](https://github.com/microsoft/onefuzz/pull/2649)
* Agent: Pass the `machine_id` from the Agent to the Task. [#2662](https://github.com/microsoft/onefuzz/pull/2662)

### Changed

* Service: Deployment enables refactored C# App Function. [#2650](https://github.com/microsoft/onefuzz/pull/2650)
* CLI: Attempt to use broker or browser login instead of device flow for authentication. Canceling the attempt with `Ctrl-C` will fall back to using the device flow. [#2612](https://github.com/microsoft/onefuzz/pull/2612)
* Service: Update to .NET 7. [#2615](https://github.com/microsoft/onefuzz/pull/2615)
* Service: Make Proxy `TelemetryKey` optional. [#2619](https://github.com/microsoft/onefuzz/pull/2619)
* Service: Update OMI to 1.6.10.2 on Ubuntu VMs. [#2629](https://github.com/microsoft/onefuzz/pull/2629)
* CLI: Make the `--container_type` parameter required when using the `containers` command. [#2631](https://github.com/microsoft/onefuzz/pull/2631)
* Service: Improve logging around notification failures. [#2653](https://github.com/microsoft/onefuzz/pull/2653)
* Service: Standardize HTTP Error Results. Better Rejection Message When Parsing Validated Strings. [#2663](https://github.com/microsoft/onefuzz/pull/2663)
* CLI: Retry on Connection Errors when acquiring auth token. [#2668](https://github.com/microsoft/onefuzz/pull/2668)

### Fixed

* Service: Notification Template `targetUrl` parameter fix. Only use the filename instead of the absolute path in the URL. The makes the links created in ADO bugs work as expected. [#2625](https://github.com/microsoft/onefuzz/pull/2625)
* CLI: Fixed SignalR client code not reading responses correctly. [#2626](https://github.com/microsoft/onefuzz/pull/2626)
* Service: Fix a logic bug in the notification hook. [#2627](https://github.com/microsoft/onefuzz/pull/2627)
* Service: Bug fixes related to the unmanaged nodes (an unreleased feature). [#2632](https://github.com/microsoft/onefuzz/pull/2632)
* Service: Fix invocation of `functionapp` in the deployment script. Where the wrong value/parameter pair were used. [#2645](https://github.com/microsoft/onefuzz/pull/2645)
* Service: Fixing .NET crash report no-repro. [#2642](https://github.com/microsoft/onefuzz/pull/2642)
* Service: Check Extensions Status Before Transitioning to `running` state during VM setup. [#2667](https://github.com/microsoft/onefuzz/pull/2667)

## 5.20.0

### Added

* Service: Added endpoint to download agent binaries to support the unmanaged node scenario. [#2600](https://github.com/microsoft/onefuzz/pull/2600)
* Service: Added additional error handling when updating VMSS nodes. [#2607](https://github.com/microsoft/onefuzz/pull/2607)

### Changed

* Service: Added additional logging when using the `decommission` node policy. [#2605](https://github.com/microsoft/onefuzz/pull/2605)

* Agent/Supervisor/Proxy: Updated third-party Rust dependencies. [#2608](https://github.com/microsoft/onefuzz/pull/2608)
* Service: Added optional `retry_limit` when connecting to the repro machine. [#2609](https://github.com/microsoft/onefuzz/pull/2609)

### Fixed

* Service: Fixed `status top` in C# implementation. [#2604](https://github.com/microsoft/onefuzz/pull/2604)
* Service: Only add "re-opened" comments to a bug if it was actually reopened. [#2623](https://github.com/microsoft/onefuzz/pull/2623)

## 5.19.0

### Changed

* Service: Delete nodes once they're done with tasks instead of releasing scale-in protection. [#2586](https://github.com/microsoft/onefuzz/pull/2586)
* Service: Switch to using the package provided by Azure Functions to set up Application Insights and improve its reporting of OneFuzz transactions. [#2597](https://github.com/microsoft/onefuzz/pull/2597)

### Fixed

* Service: Fix handling duplicate containers across accounts in C# functions. [#2596](https://github.com/microsoft/onefuzz/pull/2596)
* Service: Fix the notification GET request on C# endpoints. [#2591](https://github.com/microsoft/onefuzz/pull/2591)

## 5.18.0

### Added

* Service: Use records to unpack the request parameters in `AgentRegistration`. [#2570](https://github.com/microsoft/onefuzz/pull/2570)
* Service: Convert ADO traces to `customEvents` and update `notificationInfo`. [#2508](https://github.com/microsoft/onefuzz/pull/2508)
* Agent: Include computer name in `AgentRegistration` & decode Instance ID from it. This will reduce the amount of calls to Azure minimizing throttling errors. [#2557](https://github.com/microsoft/onefuzz/pull/2557)

### Changed

* Service: Improve webhook logging and accept more HTTP success codes. [#2568](https://github.com/microsoft/onefuzz/pull/2568)
* Service: Reduce fetches to VMSS [#2577](https://github.com/microsoft/onefuzz/pull/2577)
* CLI: Use the virtual env folder to store the config if it exists. [#2561](https://github.com/microsoft/onefuzz/pull/2561), [#2567](https://github.com/microsoft/onefuzz/pull/2567), [#2583](https://github.com/microsoft/onefuzz/pull/2583)

### Fixed

* Service: Reduce number of ARM calls in `ListVmss` reducing calls to Azure to prevent throttling. [#2539](https://github.com/microsoft/onefuzz/pull/2539)
* Service: ETag updated in `Update` and `Replace`. [#2562](https://github.com/microsoft/onefuzz/pull/2562)
* Service: Don't log an error if we delete a Repro and it is already missing. [#2563](https://github.com/microsoft/onefuzz/pull/2563)

## 5.17.0

### Added

* Service: Added exponential backoff for failed notifications. Many of the failures are a result of ADO throttling. [#2555](https://github.com/microsoft/onefuzz/pull/2555)
* Service: Add a `DeleteAll` operation to ORM that speeds up the deletion of multiple entities. [#2519](https://github.com/microsoft/onefuzz/pull/2519)

### Changed

* Documentation: Remove suggestion to reset `IterationPath` upon duplicate. [#2533](https://github.com/microsoft/onefuzz/pull/2533)
* Service: Ignoring the scanning log file when reporting an issue with azcopy. [#2536](https://github.com/microsoft/onefuzz/pull/2536)

### Fixed

* CLI: Fixed failures in command `$ onefuzz status pool <pool_name>`. [#2551](https://github.com/microsoft/onefuzz/pull/2551)
* Deployment: Fix the OneFuzz web address that is used to generate the `input_url` for bug reporting. [#2543](https://github.com/microsoft/onefuzz/pull/2543)
* Service: Produce an error if coverage recording failed due to a timeout. [#2529](https://github.com/microsoft/onefuzz/pull/2529)
* Service: Increased the default timeout for coverage recording from 5 seconds to 120 to prevent premature errors while parsing symbols and executables. [#2556](https://github.com/microsoft/onefuzz/pull/2556)
* Service: Fixed errors in ADO notifications to reduce duplicate bug-filing. [#2534](https://github.com/microsoft/onefuzz/pull/2534)
* Service: Handle null values better in `ScalesetOperations` and `VmssOperations` when a scaleset is in shutdown state. [#2538](https://github.com/microsoft/onefuzz/pull/2538)
* Service: Fix exception message formatting in `VmssOperations`. [#2546](https://github.com/microsoft/onefuzz/pull/2546)
* Service: Downgrade instance not found exception. [#2549](https://github.com/microsoft/onefuzz/pull/2549)
* Service: Lower log level on symbol region overlap findings during coverage recording. [#2559](https://github.com/microsoft/onefuzz/pull/2559)

## 5.16.0

### Added

* Documentation: Added OneFuzz logo to the README file. [#2340](https://github.com/microsoft/onefuzz/pull/2340)
* Agent: Added try_insert function when building code coverage maps. [#2510](https://github.com/microsoft/onefuzz/pull/2510)

### Changed

* Documentation: Described the importance of using the right runtime identifier (RID) when building .NET binaries. [#2490](https://github.com/microsoft/onefuzz/pull/2490)
* Service: Downgraded logging statement from `error` to `warn` and also included the http result code. [#2484](https://github.com/microsoft/onefuzz/pull/2484)
* Service/CLI: Updated python dependencies. [#2470](https://github.com/microsoft/onefuzz/pull/2470)
* Service: Updated the verbosity of `azcopy` logging to assist in debugging copy failures. [#2598](https://github.com/microsoft/onefuzz/pull/2498)
* Agent/Supervisor/Proxy: Updated third-party Rust dependencies.[#2500](https://github.com/microsoft/onefuzz/pull/2500)
* Service: Update the logic for checking if a blob exists before uploading to reduce contention during uploads. [#2503](https://github.com/microsoft/onefuzz/pull/2503)
* Service: Changed the way we update the `scaleInProtection` on a scaleset node to minimize throttling. [#2505](https://github.com/microsoft/onefuzz/pull/2505)

### Fixed

* Service: Only fetch InstanceView data when required. This will reduce throttling by Azure.[#2506](https://github.com/microsoft/onefuzz/pull/2506)
* Service: Fixed github notification queries in the C# implementation (currently not turned on). [#2513](https://github.com/microsoft/onefuzz/pull/2513), [#2514](https://github.com/microsoft/onefuzz/pull/2514)

## 5.15.1

### Added

* Service: Added support for Jinja template migration to Scriban. [#2486](https://github.com/microsoft/onefuzz/pull/2486)

### Fixed

* Service: Replaced missing tab that caused ADO queries to fail to find existing work items resulting in duplicate items being created. [#2492](https://github.com/microsoft/onefuzz/pull/2492)
* Tests: Fixed `integration-tests-linux`. [#2487](https://github.com/microsoft/onefuzz/pull/2487)

## 5.15.0

### Added

* Service: Use `InterpolatedStringHandler` to move values to `CustomDimensions` Tags [#2450](https://github.com/microsoft/onefuzz/pull/2450)
* Service: C# Can create ADO notifications [#2456](https://github.com/microsoft/onefuzz/pull/2456), [#2458](https://github.com/microsoft/onefuzz/pull/2458)
* Service: C# Cache VMSS VM InstanceID lookups [#2464](https://github.com/microsoft/onefuzz/pull/2464)
* CLI: Retry on connection reset [#2468](https://github.com/microsoft/onefuzz/pull/2468)
* Agent: Enable backtraces for agent errors [#2437](https://github.com/microsoft/onefuzz/pull/2437)

### Changed

* Service: Bump Dependencies [#2446](https://github.com/microsoft/onefuzz/pull/2446)
* Service: Temporarily disable Pool validation [#2459](https://github.com/microsoft/onefuzz/pull/2459)

### Fixed

* Service: Fix logic to retrieve partitionKey and rowKey [#2447](https://github.com/microsoft/onefuzz/pull/2447)
* Service: Permit periods in Pool names [#2452](https://github.com/microsoft/onefuzz/pull/2452)
* Service: Node state getting reset to init [#2454](https://github.com/microsoft/onefuzz/pull/2454)
* Service: Fix null ref exception in C# logging [#2460](https://github.com/microsoft/onefuzz/pull/2460)
* Service: Correct pool transitions [#2462](https://github.com/microsoft/onefuzz/pull/2462)
* Service: Fix UpdateConfigs [#2463](https://github.com/microsoft/onefuzz/pull/2463)
* Service: Allow worker loops to continue after errors [#2469](https://github.com/microsoft/onefuzz/pull/2469)
* Service: Lowercase webhooks digest header value [#2471](https://github.com/microsoft/onefuzz/pull/2471)
* Service: Fix C# Node state machine. [#2476](https://github.com/microsoft/onefuzz/pull/2476)
* Service: Adding missing caching from python code [#2467](https://github.com/microsoft/onefuzz/pull/2467)

## 5.14.0

### Added

* Service: Implement not-implemented `GetInputContainerQueues` [#2380](https://github.com/microsoft/onefuzz/pull/2380)
* Service: Adding new default image config value to instance config [2434](https://github.com/microsoft/onefuzz/pull/2434)

### Changed

* Service: Port `SyncAutoscaleSettings` from Python to C# [#2407](https://github.com/microsoft/onefuzz/pull/2407)

### Fixed

* Deployment: Updating error and fixing default value for `auto_create_cli_app` [#2378](https://github.com/microsoft/onefuzz/pull/2378)
* Service: Do not discard proxy objects when setting state [#2441](https://github.com/microsoft/onefuzz/pull/2441)
* Service: Do not fail task on notification failure [#2435](https://github.com/microsoft/onefuzz/pull/2435)
* Service: Cleanup queues for non-existent pools and non-existent tasks [#2433](https://github.com/microsoft/onefuzz/pull/2433)
* Service: Delete pool queue when pool is deleted [#2431](https://github.com/microsoft/onefuzz/pull/2431)
* Service: Minor fixes to service logging and error handling [#2420](https://github.com/microsoft/onefuzz/pull/2420)
* Service: Fixed linux repro extensions [#2415](https://github.com/microsoft/onefuzz/pull/2415)
* Service: Mark tasks as failed if a work unit cannot be created for the task [#2409](https://github.com/microsoft/onefuzz/pull/2409)
* Service: Fixed several bugs in C# ports for `TimerProxy`, `TimerRetention`, `AgentEvents`, `Node`, `Tasks` and  `Jobs` [#2406](https://github.com/microsoft/onefuzz/pull/2406), [#2392](https://github.com/microsoft/onefuzz/pull/2392), [#2379](https://github.com/microsoft/onefuzz/pull/2379)
* Service: Fixed Azure linux instance proxy extensions provisioning failures [#2401](https://github.com/microsoft/onefuzz/pull/2401)
* Service: Fixed C# scheduling bugs [#2390](https://github.com/microsoft/onefuzz/pull/2390)
* Service: Fixed `MarkDependantsFailed` error checking [#2389](https://github.com/microsoft/onefuzz/pull/2389)
* Service: Fixed `SearchStates` querying in `TaskOperations` [#2383](https://github.com/microsoft/onefuzz/pull/2383)
* Service: Fixed Scaleset response Auth inclusion [#2382](https://github.com/microsoft/onefuzz/pull/2382)
* Service: Fix custom type interpolation in queries [#2376](https://github.com/microsoft/onefuzz/pull/2376)
* Service: Fixed error in C# port for `DoNotRunExtensionsOnOverprovisionedVms must be false if Overprovision is false` [#2375](https://github.com/microsoft/onefuzz/pull/2375)

## 5.13.0

### Added

* Deployment: Added optional flags `--onefuzz_app_id` & `--auto_create_cli_app` for `deploy.py` to allow for custom app registrations. [#2305](https://github.com/microsoft/onefuzz/pull/2305)
* Deployment: Added optional flag `--host_dotnet_on_windows` for `deploy.py` that enables running dotnet functions on Windows based hosts to allow for attaching a remote debugger [#2344](https://github.com/microsoft/onefuzz/pull/2344)
* Deployment: Added optional flag `--enable_profiler` for `deploy.py` to enable memory and cpu profilers in dotnet Azure functions [#2345](https://github.com/microsoft/onefuzz/pull/2345)
* Service: Enabled AppInsights dependency tracking to enable better analysis of Azure Storage usage on OneFuzz deployments[#2315](https://github.com/microsoft/onefuzz/pull/2315)
* Service: Added `Scriban` templating library as a dependency stand-in for Jinja on C# ported services [#2330](https://github.com/microsoft/onefuzz/pull/2330)
* Service: Use 64-bit worker for dotnet functions [#2349](https://github.com/microsoft/onefuzz/pull/2349)
* Agent: Add Cobertura XML output to `src-cov` example binary [#2334](https://github.com/microsoft/onefuzz/pull/2334)
* CLI: Add `onefuzz debug task download_files <task_id> <output>` command to download a task’s containers [#2359](https://github.com/microsoft/onefuzz/pull/2359)

### Changed

* Service: Removed some response-only properties from the Task model [#2335](https://github.com/microsoft/onefuzz/pull/2335)
* Service: Create storage tables on startup [#2309](https://github.com/microsoft/onefuzz/pull/2309)
* Service: Switched to using Graph SDK instead of manually constructing queries [#2324](https://github.com/microsoft/onefuzz/pull/2324)
* Service: Cache `InstanceConfig` for improved read performance [#2329](https://github.com/microsoft/onefuzz/pull/2329)
* Deployment: Updated `deploy.py` to set all function settings at once for faster deployment and upgrades [#2325](https://github.com/microsoft/onefuzz/pull/2325)
* Devcontainer: Move global tool installs into another script so they can be cached [#2365](https://github.com/microsoft/onefuzz/pull/2365)
* Bumped several dependencies in multiple files [#2321](https://github.com/microsoft/onefuzz/pull/2321), [#2322](https://github.com/microsoft/onefuzz/pull/2322), [#2360](https://github.com/microsoft/onefuzz/pull/2360), [#2361](https://github.com/microsoft/onefuzz/pull/2361), [#2364](https://github.com/microsoft/onefuzz/pull/2364), [#2355](https://github.com/microsoft/onefuzz/pull/2355)

### Fixed

* Service: Fix `az_copy` syncing issues by removing the `max_elapsed_time limit` and relying on `RETRY_COUNT` instead [#2332](https://github.com/microsoft/onefuzz/pull/2332)
* Service: Implement not implemented bits in Scaleset/VMSS Operations for `RemiageNodes` & `DeleteNodes`[#2341](https://github.com/microsoft/onefuzz/pull/2341)
* Service: Fixed bugs in C# port of `Proxy` and `TimerProxy` functions [#2317](https://github.com/microsoft/onefuzz/pull/2317), [#2333](https://github.com/microsoft/onefuzz/pull/2333)
* Service: Enforce that there are no extra properties in request JSON, and that non-null properties are `[Required]` [#2328](https://github.com/microsoft/onefuzz/pull/2328)
* Service: Remove `IDisposable` from `Creds` [#2327](https://github.com/microsoft/onefuzz/pull/2327)
* Service: Removed required field in `Requests` to match python behavior [#2367](https://github.com/microsoft/onefuzz/pull/2367)
* Service: Fixed bug in Azure DevOps notification information [#2368](https://github.com/microsoft/onefuzz/pull/2368)
* Service: Fixed memory leaks in `AgentEvents` and several supporting libraries [#2356](https://github.com/microsoft/onefuzz/pull/2356)
* Service: Fixed inconsistencies in VMSS creation between C#/Python functions [#2358](https://github.com/microsoft/onefuzz/pull/2358)
* Service: Fixed dotnet `Info` function to correctly get version number from assembly attributes instead of the config [#2316](https://github.com/microsoft/onefuzz/pull/2316)
* Service: Fixed bug in python types [#2319](https://github.com/microsoft/onefuzz/pull/2319)
* Service: Fixed bugs in `timer_workers` to allow it to run properly [#2343](https://github.com/microsoft/onefuzz/pull/2343)
* CLI: Coverage task should have access to `readonly_inputs` containers [#2352](https://github.com/microsoft/onefuzz/pull/2352)
* Devcontainer: Ensure that python virtual environment is installed [#2372](https://github.com/microsoft/onefuzz/pull/2372)

## 5.12.0

### Added

* Deployment: Add `--use_dotnet_agent_functions` to deploy.py. [#2292](https://github.com/microsoft/onefuzz/pull/2292)
* Service: Added Logging to `az_copy` calls for improved failure tracking. [#2303](https://github.com/microsoft/onefuzz/pull/2303)
* Service: Added Logging when sending ADO Notifications [#2291](https://github.com/microsoft/onefuzz/pull/2291)
* Service: Additional C# migration work. [#2183](https://github.com/microsoft/onefuzz/pull/2183), [#2296](https://github.com/microsoft/onefuzz/pull/2296), [#2286](https://github.com/microsoft/onefuzz/pull/2286), [#2282](https://github.com/microsoft/onefuzz/pull/2282), [#2289](https://github.com/microsoft/onefuzz/pull/2289)

### Changed

* CLI: Changed the CLI's `scaleset` commands `size` positional parameter to `max_size` to better communicate its use in auto scaling properties. [#2293](https://github.com/microsoft/onefuzz/pull/2293)

### Fixed

* Deployment: Fixed `set_admins.py` script. [#2300](https://github.com/microsoft/onefuzz/pull/2300)
* Service: Include serialization options when sending event message. [#2290](https://github.com/microsoft/onefuzz/pull/2290)

## 5.11.0

### Added

* Service: Converted remaining events to C#. [#2253](https://github.com/microsoft/onefuzz/pull/2253)
* Agent: Add the dotnet crash report task. [#2250](https://github.com/microsoft/onefuzz/pull/2250)
* Service: Additional C# migration work. [#2235](https://github.com/microsoft/onefuzz/pull/2235), [#2257](https://github.com/microsoft/onefuzz/pull/2257), [#2254](https://github.com/microsoft/onefuzz/pull/2254), [#2191](https://github.com/microsoft/onefuzz/pull/2191), [#2262](https://github.com/microsoft/onefuzz/pull/2262), [#2263](https://github.com/microsoft/onefuzz/pull/2263), [#2269](https://github.com/microsoft/onefuzz/pull/2269)

### Changed

* Agent: Increase the size of the output buffer when collecting logs from agent. [#2166](https://github.com/microsoft/onefuzz/pull/2166)
* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies. [#2270](https://github.com/microsoft/onefuzz/pull/2270)

### Fixed

* Service: Use a singleton for logging to reduce memory use. [#2247](https://github.com/microsoft/onefuzz/pull/2247)
* Service: Use a singleton for the EntityConverter. [#2267](https://github.com/microsoft/onefuzz/pull/2267)
* Service: Add retries when creating a connection to a repro machine for debugging on windows. [#2252](https://github.com/microsoft/onefuzz/pull/2252)
* Deployment: Fix deploy to use the correct date formats when querying MSGraph. [#2258](https://github.com/microsoft/onefuzz/pull/2258)
* Service: Sync the Autoscale table to current scaleset settings. [#2255](https://github.com/microsoft/onefuzz/pull/2255)
* Service: Fixed the pool comparison in the scheduler. [#2260](https://github.com/microsoft/onefuzz/pull/2260)
* CLI: Fixed the way `job` and `task` state enumerations are compared. [#2004](https://github.com/microsoft/onefuzz/pull/2004)
* Service: Clone the JsonSerializerOptions instead of just modifying it. [#2280](https://github.com/microsoft/onefuzz/pull/2280)
* Service: Fixed a NullReferenceException in CreateQueue. [#2283](https://github.com/microsoft/onefuzz/pull/2283)

## 5.10.0

### Added

* Recommendation in `getting-started.md` that OneFuzz users include a `.onefuzz` file in their project root directory for security tool detection [#2236](https://github.com/microsoft/onefuzz/pull/2236)
* Agent:  New `libfuzzer_dotnet_fuzz` task [#2221](https://github.com/microsoft/onefuzz/pull/2221)

### Changed

* CLI: Updated default Windows VM host image. [#2226](https://github.com/microsoft/onefuzz/pull/2226)
* Agent: Modified LibFuzzer struct to own its environment and option data [#2219](https://github.com/microsoft/onefuzz/pull/2219)
* Agent: Factor out generic LibFuzzer task [#2214](https://github.com/microsoft/onefuzz/pull/2214)
* Service: Enable C# migrated `TimerRetention`, `TimerDaily`, and `containers` functions [#2228](https://github.com/microsoft/onefuzz/pull/2228), [#2220](https://github.com/microsoft/onefuzz/pull/2220), [#2197](https://github.com/microsoft/onefuzz/pull/2197)
* Service: Finished migrating `TimerRepro` to C# [#2222](https://github.com/microsoft/onefuzz/pull/2222), [#2216](https://github.com/microsoft/onefuzz/pull/2216), [#2218](https://github.com/microsoft/onefuzz/pull/2218)
* Service: Change instances of `NotImplementedException` to more accurately be `NotSupportedException` exceptions [#2234](https://github.com/microsoft/onefuzz/pull/2234)
* Service: Migrated `Tasks`, `Notifications`, `add_node_ssh_key`,  and `Proxy` functions to C# [#2233](https://github.com/microsoft/onefuzz/pull/2233), [#2188](https://github.com/microsoft/onefuzz/pull/2188), [#2193](https://github.com/microsoft/onefuzz/pull/2193), [#2206](https://github.com/microsoft/onefuzz/pull/2206), [#2200](https://github.com/microsoft/onefuzz/pull/2200)

### Fixed

* Service: Update the autoscale settings to allow a VM scaleset to scale down to zero nodes and prevent new nodes from spinning up when in the `shutdown` state. [#2232](https://github.com/microsoft/onefuzz/pull/2232), [#2248](https://github.com/microsoft/onefuzz/pull/2248)
* Service: Add a missing function call to properly queue webhook events in `WebhookOperations` [#2231](https://github.com/microsoft/onefuzz/pull/2231)
* Service: Add a missing job state transition to the `Task` implementation. [#2202](https://github.com/microsoft/onefuzz/pull/2202)
* Service: Fixed the return value in the C# implementation when associating a subnet with the NSG. [#2201](https://github.com/microsoft/onefuzz/pull/2201)
* Service: Changed log level from `Error` to `Info` in `TimerProxy`. [#2185](https://github.com/microsoft/onefuzz/pull/2185)
* Service: Fixed `TimerTasks` config bugs in the C# port. [#2196](https://github.com/microsoft/onefuzz/pull/2196)

## 5.9.0

### Added

* Agent: Depend on SharpFuzz 2.0.0 package in the `LibFuzzerDotnetLoader` project. [#2149](https://github.com/microsoft/onefuzz/pull/2149)
* Test: Added `GoodBad` C# example project to use with the `LibFuzzerDotnetLoader` integration tests. [#2148](https://github.com/microsoft/onefuzz/pull/2148)

### Changed

* Service: Implemented the `containers` function in C#. [#2078](https://github.com/microsoft/onefuzz/pull/2078)
* Service/Build: Reuse agent build artifacts if nothing in the agent source tree has changed. This is to speed up dev builds and will not impact official releases. [#2115](https://github.com/microsoft/onefuzz/pull/2115)
* CLI: Default autoscale minimum value to 0. This allows a scaleset to scale-in until there are zero nodes running when no work is pending in the queue. This is important to ensure VM's have the latest patches when running. [#2112](https://github.com/microsoft/onefuzz/pull/2112), [#2162](https://github.com/microsoft/onefuzz/pull/2162)
* Service: Initial work to migrate `TimerRepro` function to C#. [#2168](https://github.com/microsoft/onefuzz/pull/2168)
* Service/CLI: Remove support for pre 3.0.0 style authentication. [#2173](https://github.com/microsoft/onefuzz/pull/2173)
* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies. [#2164](https://github.com/microsoft/onefuzz/pull/2164), [#2056](https://github.com/microsoft/onefuzz/pull/2056), [#2175](https://github.com/microsoft/onefuzz/pull/2175), [#2127](https://github.com/microsoft/onefuzz/pull/2127)
* Service: Updated C# dependencies. [#2181](https://github.com/microsoft/onefuzz/pull/2181)
* Service: When getting information about a task, ignore the task's state if the `job_id` is specified. [#2171](https://github.com/microsoft/onefuzz/pull/2171)

### Fixed

* Agent: Drop the global event sender when closing the telemetry channel to ensure all events are flushed. [#2125](https://github.com/microsoft/onefuzz/pull/2125)

## 5.8.0

### Added

* Service: Add correct routes and auth to agent C# functions. [#2109](https://github.com/microsoft/onefuzz/pull/2109)
* Service: Port `agent_registration` to C#. [#2107](https://github.com/microsoft/onefuzz/pull/2107)
* Agent: Add the `dotnet_coverage` task. [#2062](https://github.com/microsoft/onefuzz/pull/2062)
* Agent: Add multiple ways to specify LibFuzzerDotnetLoader targets. [#2136](https://github.com/microsoft/onefuzz/pull/2136)
* Agent: Add logging to LibFuzzerDotnetLoader. [#2141](https://github.com/microsoft/onefuzz/pull/2141)
* Documentation: Added documentation for LibFuzzerDotnetLoader. [#2142](https://github.com/microsoft/onefuzz/pull/2142)

### Changed

* Service: Add caching to C# storage implementation so repeated queries do not get throttled. [#2102](https://github.com/microsoft/onefuzz/pull/2102)
* Service: Remove unused poolname validation. [#2094](https://github.com/microsoft/onefuzz/pull/2094)
* Service: Make the hostbuilder async in C#. [#2122](https://github.com/microsoft/onefuzz/pull/2122)
* Service: Updated the scaling policy for the App Functions. [#2140](https://github.com/microsoft/onefuzz/pull/2140)
* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies. [#2022](https://github.com/microsoft/onefuzz/pull/2022)
* Agent: Remove incomplete macOS support. [#2134](https://github.com/microsoft/onefuzz/pull/2134), [#2135](https://github.com/microsoft/onefuzz/pull/2135)

### Fixed

* Service: Include State filter when searching for expired tasks and jobs. [#2138](https://github.com/microsoft/onefuzz/pull/2138)
* Service: Fix reported TLS errors. [#2087](https://github.com/microsoft/onefuzz/pull/2087)
* Service: Change the `upload_file` method to use the Azcopy command by default for robustness and fall back to the Azure Python SDK implementation if needed. This also addresses issues where low bandwidth connections timeout due to not being able to handle multiple concurrent upload streams.  [#1556](https://github.com/microsoft/onefuzz/pull/1556)
* Service: Update the log SAS URL to last as long as the job duration. [#2116](https://github.com/microsoft/onefuzz/pull/2116), [#2121](https://github.com/microsoft/onefuzz/pull/2121)
* Service: Fixed a number of issues in the C# implementation of `TimerProxy`. [#2133](https://github.com/microsoft/onefuzz/pull/2133)
* Agent: Fix a race condition when monitoring files on the VM. [#2105](https://github.com/microsoft/onefuzz/pull/2105)

## 5.7.1

This a hotpatch to the 5.7.0 release fixing SAS URL generation which had the potential to cause tasks to fail. [#2116](https://github.com/microsoft/onefuzz/pull/2116)

## 5.7.0

### Added

* Agent: Add `NodeState` to Node Heartbeat to better track the current state of nodes in the system [#2024](https://github.com/microsoft/onefuzz/pull/2024), [#2053](https://github.com/microsoft/onefuzz/pull/2053)
* Service: Ported existing Python functions to C# [#2061](https://github.com/microsoft/onefuzz/pull/2061), [#2072](https://github.com/microsoft/onefuzz/pull/2072), [#2076](https://github.com/microsoft/onefuzz/pull/2076), [#2066](https://github.com/microsoft/onefuzz/pull/2066)
* Service: Enabling ported C# functions for `QueueNodeHeartbeat`, `QueueTaskHeartbeat`, and `QueueSignalREvents`  [#2046](https://github.com/microsoft/onefuzz/pull/2046), [#2047](https://github.com/microsoft/onefuzz/pull/2047)
* Service: Add null analysis attributes to service result types to make it easier to check and use the various existing result types [#2069](https://github.com/microsoft/onefuzz/pull/2069)
* Service: Add dotnet editorconfig underscores naming rule for private fields to start with an underscore, ensuring OmniSharp will generate conformant names by default [#2070](https://github.com/microsoft/onefuzz/pull/2070)

### Changed

* Agent: Update onefuzz-agent clap to version `3.2.4` [#2049](https://github.com/microsoft/onefuzz/pull/2049)
* Agent: Added scripts to install dotnet on windows and ubuntu fuzzing VMs [#2038](https://github.com/microsoft/onefuzz/pull/2038)
* Deployment: Update Getting Started instructions for `deploy.py`'s file permissions. [#2030](https://github.com/microsoft/onefuzz/pull/2030)

### Fixed

* CLI: Error output to specify that the tools are missing locally, not on the repro VM [#2036](https://github.com/microsoft/onefuzz/pull/2036)
* Service: Handle service event messages that are too big to fit in a queue message. [#2020](https://github.com/microsoft/onefuzz/pull/2020)
* Service: Removing unnecessary `/obj/` directory. [#2063](https://github.com/microsoft/onefuzz/pull/2063)

## 5.6.0

### Added

* Service: Add Function App settings to Bicep template and `deploy.py`. [#1973](https://github.com/microsoft/onefuzz/pull/1973)
* Agent: Add a timestamp to agent log to make it easier to correlate events. [#1972](https://github.com/microsoft/onefuzz/pull/1972)

### Changed

* Agent/Supervisor/Proxy: Rename the supervisor process from `onefuzz-supervisor` to `onefuzz-agent`. [#1989](https://github.com/microsoft/onefuzz/pull/1989)
* Agent/Supervisor/Proxy: Rename the task executor that runs on the VM from `onefuzz-agent` to `onefuzz-task`. [#1980](https://github.com/microsoft/onefuzz/pull/1980)
* Agent/Supervisor/Proxy: Ensure `GlobalFlag` registry value is initialized for targets. [#1960](https://github.com/microsoft/onefuzz/pull/1960)
* Agent/Supervisor/Proxy: Enable full backtraces on Rust panics. [#1959](https://github.com/microsoft/onefuzz/pull/1959)
* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies. [#2003](https://github.com/microsoft/onefuzz/pull/2003), [#2002](https://github.com/microsoft/onefuzz/pull/2002), [#1999](https://github.com/microsoft/onefuzz/pull/1999), [#1992](https://github.com/microsoft/onefuzz/pull/1992), [#1986](https://github.com/microsoft/onefuzz/pull/1986), [#1983](https://github.com/microsoft/onefuzz/pull/1983), [#1982](https://github.com/microsoft/onefuzz/pull/1982), [#1981](https://github.com/microsoft/onefuzz/pull/1981), [#1985](https://github.com/microsoft/onefuzz/pull/1985), [#1974](https://github.com/microsoft/onefuzz/pull/1974), [#1969](https://github.com/microsoft/onefuzz/pull/1969), [#1965](https://github.com/microsoft/onefuzz/pull/1965)
* CLI/Service: Updated multiple first-party and third-party Python dependencies. [#2009](https://github.com/microsoft/onefuzz/pull/2009), [#1996](https://github.com/microsoft/onefuzz/pull/1996)

### Fixed

* Agent: Remove stray print statement from the task_logger. [#1975](https://github.com/microsoft/onefuzz/pull/1975)
* Agent: Fix local coverage definition by removing a duplicated command line parameter. [#1962](https://github.com/microsoft/onefuzz/pull/1962)
* Service: Fix Instance Config and Management Logic. [#2016](https://github.com/microsoft/onefuzz/pull/2016)

## 5.5.0

### Added

* Service: Added new functionality to the service port from Python to C#. [#1924](https://github.com/microsoft/onefuzz/pull/1924), [#1938](https://github.com/microsoft/onefuzz/pull/1938), [#1946](https://github.com/microsoft/onefuzz/pull/1946), [#1934](https://github.com/microsoft/onefuzz/pull/1934)

### Changed

* Documentation: Update coverage filtering docs. [#1950](https://github.com/microsoft/onefuzz/pull/1950)
* Agent: Allow the agent to skip reporting directories. [#1931](https://github.com/microsoft/onefuzz/pull/1931)
* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies. [#1935](https://github.com/microsoft/onefuzz/pull/1935)
* CLI: Deprecate `libfuzzer_coverage` task. [#1944](https://github.com/microsoft/onefuzz/pull/1944)
* CLI: Use consistent logger names. [#1945](https://github.com/microsoft/onefuzz/pull/1945)
* Service: Updated functionality to the service port from Python to C#. [#1922](https://github.com/microsoft/onefuzz/pull/1922), [#1925](https://github.com/microsoft/onefuzz/pull/1925), [#1947](https://github.com/microsoft/onefuzz/pull/1947)

### Fixed

* Agent: Allow old reports to be parsed. [#1943](https://github.com/microsoft/onefuzz/pull/1943)
* Agent: Remove transitive OpenSSL dependency. [#1952](https://github.com/microsoft/onefuzz/pull/1952)
* Agent: Ensure `GlobalFlag` registry value is initialized when checking library dependencies. [#1960](https://github.com/microsoft/onefuzz/pull/1960)
* Service: Allow old reports to be parsed. [#1940](https://github.com/microsoft/onefuzz/pull/1940)

## 5.4.1

This a hotpatch to the 5.4.0 release fixing the parsing failures from old crash reports.

### Fixed

* Agent: Allow old reports to be parsed [#1943](https://github.com/microsoft/onefuzz/pull/1943)
* Agent: Include `LD_LIBRARY_PATH` in shared library dependency check if and only if set by command. [#1933](https://github.com/microsoft/onefuzz/pull/1933)
* Service: Allow old reports to be parsed [#1940](https://github.com/microsoft/onefuzz/pull/1940)

## 5.4.0

### Added

* Agent: Added the OneFuzz version and tool name to the Crash Report. [#1635](https://github.com/microsoft/onefuzz/pull/1635)
* Agent: Added a check for missing libraries when running the LibFuzzer `-help` check. [#1812](https://github.com/microsoft/onefuzz/pull/1812)
* Service: Added new functionality to the service port from Python to C#. [#1794](https://github.com/microsoft/onefuzz/pull/1794), [#1813](https://github.com/microsoft/onefuzz/pull/1813),  [#1814](https://github.com/microsoft/onefuzz/pull/1814), [#1818](https://github.com/microsoft/onefuzz/pull/1818), [#1820](https://github.com/microsoft/onefuzz/pull/1820), [#1821](https://github.com/microsoft/onefuzz/pull/1821), [#1830](https://github.com/microsoft/onefuzz/pull/1830), [#1832](https://github.com/microsoft/onefuzz/pull/1832), [#1833](https://github.com/microsoft/onefuzz/pull/1833), [#1835](https://github.com/microsoft/onefuzz/pull/1835), [#1836](https://github.com/microsoft/onefuzz/pull/1836), [#1838](https://github.com/microsoft/onefuzz/pull/1838), [#1839](https://github.com/microsoft/onefuzz/pull/1839), [#1841](https://github.com/microsoft/onefuzz/pull/1841), [#1845](https://github.com/microsoft/onefuzz/pull/1845), [#1846](https://github.com/microsoft/onefuzz/pull/1846), [#1847](https://github.com/microsoft/onefuzz/pull/1847), [#1848](https://github.com/microsoft/onefuzz/pull/1848), [#1851](https://github.com/microsoft/onefuzz/pull/1851), [#1852](https://github.com/microsoft/onefuzz/pull/1852), [#1853](https://github.com/microsoft/onefuzz/pull/1853), [#1854](https://github.com/microsoft/onefuzz/pull/1854), [#1855](https://github.com/microsoft/onefuzz/pull/1855), [#1860](https://github.com/microsoft/onefuzz/pull/1860), [#1861](https://github.com/microsoft/onefuzz/pull/1861), [#1863](https://github.com/microsoft/onefuzz/pull/1863), [#1870](https://github.com/microsoft/onefuzz/pull/1870), [#1875](https://github.com/microsoft/onefuzz/pull/1875), [#1876](https://github.com/microsoft/onefuzz/pull/1876), [#1878](https://github.com/microsoft/onefuzz/pull/1878), [#1879](https://github.com/microsoft/onefuzz/pull/1879), [#1880](https://github.com/microsoft/onefuzz/pull/1880), [#1884](https://github.com/microsoft/onefuzz/pull/1884), [#1885](https://github.com/microsoft/onefuzz/pull/1885), [#1886](https://github.com/microsoft/onefuzz/pull/1886), [#1887](https://github.com/microsoft/onefuzz/pull/1887), [#1888](https://github.com/microsoft/onefuzz/pull/1888), [#1895](https://github.com/microsoft/onefuzz/pull/1895), [#1897](https://github.com/microsoft/onefuzz/pull/1897), [#1898](https://github.com/microsoft/onefuzz/pull/1898), [#1899](https://github.com/microsoft/onefuzz/pull/1899), [#1903](https://github.com/microsoft/onefuzz/pull/1903), [#1904](https://github.com/microsoft/onefuzz/pull/1904), [#1905](https://github.com/microsoft/onefuzz/pull/1905), [#1907](https://github.com/microsoft/onefuzz/pull/1907), [#1909](https://github.com/microsoft/onefuzz/pull/1909), [#1910](https://github.com/microsoft/onefuzz/pull/1910), [#1912](https://github.com/microsoft/onefuzz/pull/1912)
* Service: Restrict node operations to administrators. [#1779](https://github.com/microsoft/onefuzz/pull/1779)

### Changed

* CLI/Service: Updated multiple first-party and third-party Python dependencies. [#1784](https://github.com/microsoft/onefuzz/pull/1784)
* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies. [#1619](https://github.com/microsoft/onefuzz/pull/1619), [#1644](https://github.com/microsoft/onefuzz/pull/1644), [#1645](https://github.com/microsoft/onefuzz/pull/1645), [#1646](https://github.com/microsoft/onefuzz/pull/1646), [#1655](https://github.com/microsoft/onefuzz/pull/1655), [#1700](https://github.com/microsoft/onefuzz/pull/1700), [#1849](https://github.com/microsoft/onefuzz/pull/1849), [#1882](https://github.com/microsoft/onefuzz/pull/1882)
* Agent: Separate `llvm-symbolizer` setup from sanitizer environment variable initialization. [#1778](https://github.com/microsoft/onefuzz/pull/1778)
* Agent: Set the TSan options based on the external symbolizer. [#1787](https://github.com/microsoft/onefuzz/pull/1787)
* CLI: Added the `ONEFUZZ_CLIENT_SECRET` environment variable and removed the `client_secret` field from the configuration file. This prevents accidental misuse via persisting the secret to disk outside of [confidential client](https://docs.microsoft.com/en-us/azure/active-directory/develop/developer-glossary#client-application) environments. If you have set a client secret in your configuration file in a public client, we recommending removing and revoking it. CI scripts that currently set the client secret in the config must instead pass it via the `ONEFUZZ_CLIENT_SECRET` environment variable or on each CLI invocation via the `--client_secret` argument. [#1918](https://github.com/microsoft/onefuzz/pull/1918)
* CLI: Use a SAS URL to download log files. [#1920](https://github.com/microsoft/onefuzz/pull/1920)

### Fixed

* Agent: Only watch directories for change events. [#1859](https://github.com/microsoft/onefuzz/pull/1859)
* Agent: Switch to a smart constructor to minimize misuse. [#1865](https://github.com/microsoft/onefuzz/pull/1865)
* Service: Fixed an issue where jobs that do not have logs configured failed to get scheduled. [#1893](https://github.com/microsoft/onefuzz/pull/1893)

## 5.3.0

### Added

* Agent: Add a compiler flag to generate debug info for the `windows-libfuzzer` load library test target. [#1684](https://github.com/microsoft/onefuzz/pull/1684)
* Agent: Add a Rust crate to debug missing dynamic library errors on Windows. [#1713](https://github.com/microsoft/onefuzz/pull/1713)
* Agent: Add support for detecting missing dynamic libraries on Linux. [#1718](https://github.com/microsoft/onefuzz/pull/1718)
* Service: Connect the auto scaling diagnostics to the log analytics workspace. [#1708](https://github.com/microsoft/onefuzz/pull/1708)
* Service: Handle the situation where a VM scale set instance is destroyed before we have removed scale-in protection. [#1719](https://github.com/microsoft/onefuzz/pull/1719)
* Service: Add additional support for auto scaling including changes to the CLI. New scale sets will automatically be created with auto scaling enabled. [#1717](https://github.com/microsoft/onefuzz/pull/1717), [#1763](https://github.com/microsoft/onefuzz/pull/1763)
* Agent/Service/CLI: Add support for generating log files that can be downloaded using the CLI. [#1727](https://github.com/microsoft/onefuzz/pull/1727), [#1723](https://github.com/microsoft/onefuzz/pull/1723), [#1721](https://github.com/microsoft/onefuzz/pull/1721)
* Service: Port ARM templates to Bicep. [#1724](https://github.com/microsoft/onefuzz/pull/1724), [#1732](https://github.com/microsoft/onefuzz/pull/1732)
* Service: Initial changes to port the service from Python to C#. [#1734](https://github.com/microsoft/onefuzz/pull/1734), [#1733](https://github.com/microsoft/onefuzz/pull/1733), [#1736](https://github.com/microsoft/onefuzz/pull/1736), [#1737](https://github.com/microsoft/onefuzz/pull/1737), [#1738](https://github.com/microsoft/onefuzz/pull/1738), [#1742](https://github.com/microsoft/onefuzz/pull/1742), [#1744](https://github.com/microsoft/onefuzz/pull/1744), [#1749](https://github.com/microsoft/onefuzz/pull/1749), [#1750](https://github.com/microsoft/onefuzz/pull/1750), [#1753](https://github.com/microsoft/onefuzz/pull/1753), [#1755](https://github.com/microsoft/onefuzz/pull/1755), [#1760](https://github.com/microsoft/onefuzz/pull/1760), [#1761](https://github.com/microsoft/onefuzz/pull/1761), [#1762](https://github.com/microsoft/onefuzz/pull/1762), [#1765](https://github.com/microsoft/onefuzz/pull/1765), [#1757](https://github.com/microsoft/onefuzz/pull/1757), [#1780](https://github.com/microsoft/onefuzz/pull/1780), [#1782](https://github.com/microsoft/onefuzz/pull/1782), [#1783](https://github.com/microsoft/onefuzz/pull/1783), [#1777](https://github.com/microsoft/onefuzz/pull/1777), [#1791](https://github.com/microsoft/onefuzz/pull/1791), [#1801](https://github.com/microsoft/onefuzz/pull/1801), [#1805](https://github.com/microsoft/onefuzz/pull/1805), [#1804](https://github.com/microsoft/onefuzz/pull/1804), [#1803](https://github.com/microsoft/onefuzz/pull/1803)
* Service: Make sure the scale set nodes are unable to accept work while in the `setup` state. [#1731](https://github.com/microsoft/onefuzz/pull/1731)

### Changed

* Agent: Reduce the logging level down from `warn` to `debug` when we are unable to parse an ASan log. [#1705](https://github.com/microsoft/onefuzz/pull/1705)
* Service: Move the creation of the event grid topic to the deployment template from the `deploy.py` script. [#1591](https://github.com/microsoft/onefuzz/pull/1591)
* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies. [#1548](https://github.com/microsoft/onefuzz/pull/1548), [#1617](https://github.com/microsoft/onefuzz/pull/1617), [#1618](https://github.com/microsoft/onefuzz/pull/1618)
* Service: Consolidate the two log analytics down to one. [#1679](https://github.com/microsoft/onefuzz/pull/1679)
* Service: Updated resource name in Bicep file to prevent name clash when deploying 5.3.0. [#1808](https://github.com/microsoft/onefuzz/pull/1808)

### Fixed

* Service: Auto scale setting log statement is not an `error` changed it to `info`. [#1745](https://github.com/microsoft/onefuzz/pull/1745)
* Agent: Fixed Cobertera output so that coverage summary renders in Azure Devops correctly. [#1728](https://github.com/microsoft/onefuzz/pull/1728)
* Agent: Continue after non-fatal errors during static recovery of SanCov coverage sites. [#1796](https://github.com/microsoft/onefuzz/pull/1796)
* Service: Fixed name generation for a few resources in the Bicep file to increase uniqueness which prevents resource name clash. [#1800](https://github.com/microsoft/onefuzz/pull/1800)

## 5.2.0

### Added

* Service: Added additional auto-scaling support for VM scale sets. [#1686](https://github.com/microsoft/onefuzz/pull/1686), [#1698](https://github.com/microsoft/onefuzz/pull/1698)

### Changed

* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies. [#1699](https://github.com/microsoft/onefuzz/pull/1699), [#1589](https://github.com/microsoft/onefuzz/pull/1589)

## 5.1.0

### Added

* Service: Added a new webhook message format compatible with Azure Event Grid. [#1640](https://github.com/microsoft/onefuzz/pull/1640)
* Service: Added initial auto scaling support for VM scale sets. [#1647](https://github.com/microsoft/onefuzz/pull/1647), [#1661](https://github.com/microsoft/onefuzz/pull/1661)
* Agent: Add an explicit timeout to setup scripts so hangs are easier to debug. [#1659](https://github.com/microsoft/onefuzz/pull/1659)

### Changed

* CLI/Service: Updated multiple first-party and third-party Python dependencies. [#1606](https://github.com/microsoft/onefuzz/pull/1606), [#1634](https://github.com/microsoft/onefuzz/pull/1634)
* Agent: Check system-wide memory usage and fail tasks that are nearly out of memory. [#1657](https://github.com/microsoft/onefuzz/pull/1657)

### Fixed

* Service: Fix `task` field to the correct `NodeTasks` type so serialization works correctly.  [#1627](https://github.com/microsoft/onefuzz/pull/1627)
* Agent: Convert escaped characters when accessing the name of a blob in a URL.  [#1673](https://github.com/microsoft/onefuzz/pull/1673)
* Agent: Override `runs` parameter when testing inputs as we only want to test them once. [#1651](https://github.com/microsoft/onefuzz/pull/1651)
* Service: Remove deprecated `warn()` method. [#1641](https://github.com/microsoft/onefuzz/pull/1641)

## 5.0.0

### Added

* CLI/Service: Added `fuzzer_target_options` argument to the `libfuzzer` templates to allow passing some target options only in persistent fuzzing mode [#1610](https://github.com/microsoft/onefuzz/pull/1610)

### Changed

* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies.
[#1530](https://github.com/microsoft/onefuzz/pull/1530)
* CLI/Service: Updated multiple first-party and third-party Python dependencies.
[#1576](https://github.com/microsoft/onefuzz/pull/1576)
[#1577](https://github.com/microsoft/onefuzz/pull/1577)
[#1579](https://github.com/microsoft/onefuzz/pull/1579)
[#1582](https://github.com/microsoft/onefuzz/pull/1582)
[#1586](https://github.com/microsoft/onefuzz/pull/1586)
[#1599](https://github.com/microsoft/onefuzz/pull/1599)
* CLI/Service: Begin update of scale set instances before reimaging to ensure they match the latest scale set model. [#1612](https://github.com/microsoft/onefuzz/pull/1612)

### Fixed

* Agent: Removed the `process_stats` telemetry event, which fixes a class of memory leaks on Windows `libfuzzer_fuzz` tasks. [#1608](https://github.com/microsoft/onefuzz/pull/1608)
* CLI/Service: Fixed seven day stale node reimaging check. [#1616](https://github.com/microsoft/onefuzz/pull/1616)

## 4.1.0

### Added

* Agent: Added source line coverage data
[#1518](https://github.com/microsoft/onefuzz/pull/1518)
[#1534](https://github.com/microsoft/onefuzz/pull/1534)
[#1538](https://github.com/microsoft/onefuzz/pull/1538)
[#1535](https://github.com/microsoft/onefuzz/pull/1535)
[#1572](https://github.com/microsoft/onefuzz/pull/1572)
* Agent: Added Cobertura XML output for source code visualization [#1533](https://github.com/microsoft/onefuzz/pull/1533)
* Service: Added auto configuration properties to the monitoring agents [#1541](https://github.com/microsoft/onefuzz/pull/1541)
* Service: Added tags to scalesets and VMs [#1560](https://github.com/microsoft/onefuzz/pull/1560)

### Changed

* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies.
[#1489](https://github.com/microsoft/onefuzz/pull/1489)
[#1495](https://github.com/microsoft/onefuzz/pull/1495)
[#1496](https://github.com/microsoft/onefuzz/pull/1496)
[#1501](https://github.com/microsoft/onefuzz/pull/1501)
[#1502](https://github.com/microsoft/onefuzz/pull/1502)
[#1507](https://github.com/microsoft/onefuzz/pull/1507)
[#1510](https://github.com/microsoft/onefuzz/pull/1510)
[#1513](https://github.com/microsoft/onefuzz/pull/1513)
[#1514](https://github.com/microsoft/onefuzz/pull/1514)
[#1517](https://github.com/microsoft/onefuzz/pull/1517)
[#1519](https://github.com/microsoft/onefuzz/pull/1519)
[#1521](https://github.com/microsoft/onefuzz/pull/1521)
[#1522](https://github.com/microsoft/onefuzz/pull/1522)
[#1528](https://github.com/microsoft/onefuzz/pull/1528)
[#1557](https://github.com/microsoft/onefuzz/pull/1557)
[#1566](https://github.com/microsoft/onefuzz/pull/1566)
* Agent: Changed the function that gets the `machine_id` to be `async` to avoid runtime nesting [#1468](https://github.com/microsoft/onefuzz/pull/1468)
* Service: Removed generic reset command from the CLI [#1511](https://github.com/microsoft/onefuzz/pull/1511)
* Service: Updated the way we check for endpoint authorization [#1472](https://github.com/microsoft/onefuzz/pull/1472)

### Fixed

* Service: Increase reliability of integration tests. [#1505](https://github.com/microsoft/onefuzz/pull/1505)
* Agent: Avoid leaking unused file and cache data [#1539](https://github.com/microsoft/onefuzz/pull/1539)
* Agent: Fixed new clippy errors [#1516](https://github.com/microsoft/onefuzz/pull/1516)

## 4.0.0

### Added

* Agent: Added common source coverage format. [#1403](https://github.com/microsoft/onefuzz/pull/1403)
* Service: Added class to store and retrieve rules associated with an API endpoint. This supports the ability to control who has access to an API. [#1420](https://github.com/microsoft/onefuzz/pull/1420)
* Service: Support for NSG creation during deployment, allowing restricted access to the scaleset and repro VMs. [#1331](https://github.com/microsoft/onefuzz/pull/1331), [#1340](https://github.com/microsoft/onefuzz/pull/1340), [#1358](https://github.com/microsoft/onefuzz/pull/1358), [#1385](https://github.com/microsoft/onefuzz/pull/1385), [#1393](https://github.com/microsoft/onefuzz/pull/1393), [#1395](https://github.com/microsoft/onefuzz/pull/1395), [#1400](https://github.com/microsoft/onefuzz/pull/1400), [#1404](https://github.com/microsoft/onefuzz/pull/1404), [#1406](https://github.com/microsoft/onefuzz/pull/1406), [#1410](https://github.com/microsoft/onefuzz/pull/1410)
* Service: Guest account access is disabled by default when creating the default service principal during deployment. [#1425](https://github.com/microsoft/onefuzz/pull/1425)
* Service: Group membership check added. [#1074](https://github.com/microsoft/onefuzz/pull/1074)
* Service: Exposed the `target_timeout` parameter in the `radamsa basic` template. [#1499](https://github.com/microsoft/onefuzz/pull/1499)

### Changed

* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies. [#1360](https://github.com/microsoft/onefuzz/pull/1360), [#1364](https://github.com/microsoft/onefuzz/pull/1364), [#1367](https://github.com/microsoft/onefuzz/pull/1367), [#1368](https://github.com/microsoft/onefuzz/pull/1368), [#1369](https://github.com/microsoft/onefuzz/pull/1369), [#1382](https://github.com/microsoft/onefuzz/pull/1382), [#1429](https://github.com/microsoft/onefuzz/pull/1429), [#1455](https://github.com/microsoft/onefuzz/pull/1455), [#1456](https://github.com/microsoft/onefuzz/pull/1456), [#1414](https://github.com/microsoft/onefuzz/pull/1414), [#1416](https://github.com/microsoft/onefuzz/pull/1416), [#1417](https://github.com/microsoft/onefuzz/pull/1417), [#1423](https://github.com/microsoft/onefuzz/pull/1423), [#1438](https://github.com/microsoft/onefuzz/pull/1438), [#1446](https://github.com/microsoft/onefuzz/pull/1446), [#1458](https://github.com/microsoft/onefuzz/pull/1458), [#1463](https://github.com/microsoft/onefuzz/pull/1463), [#1470](https://github.com/microsoft/onefuzz/pull/1470), [#1453](https://github.com/microsoft/onefuzz/pull/1453), [#1492](https://github.com/microsoft/onefuzz/pull/1492), [#1493](https://github.com/microsoft/onefuzz/pull/1493), [#1480](https://github.com/microsoft/onefuzz/pull/1480), [#1488](https://github.com/microsoft/onefuzz/pull/1488), [#1490](https://github.com/microsoft/onefuzz/pull/1490)

### Fixed

* Service: Fixed Azure DevOps work item creation by adding missing client initialization. [#1370](https://github.com/microsoft/onefuzz/pull/1370)
* Service: Fixed validation of the `target_exe` blob name, enabling nesting in a subdirectory of the `setup` container. [#1371](https://github.com/microsoft/onefuzz/pull/1371)
* Service: Migrated to MS Graph, as `azure-graphrbac` is soon to be deprecated. [#966](https://github.com/microsoft/onefuzz/pull/966)
* Service: Stopped ignoring unexpected errors when authenticating the client secret. [#1376](https://github.com/microsoft/onefuzz/pull/1376)
* Service: Fixed regex to correctly capture the object ID when trying to remove an invalid application ID. [#1408](https://github.com/microsoft/onefuzz/pull/1408)
* Service: Added check for service principal use during user role assignment. [#1479](https://github.com/microsoft/onefuzz/pull/1479)
* Service: Added support for Compute Gallery images. [#1450](https://github.com/microsoft/onefuzz/pull/1450)

## 3.2.0

### Changed

* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies. [#1301](https://github.com/microsoft/onefuzz/pull/1301), [#1302](https://github.com/microsoft/onefuzz/pull/1302), [#1310](https://github.com/microsoft/onefuzz/pull/1310), [#1312](https://github.com/microsoft/onefuzz/pull/1312), [#1332](https://github.com/microsoft/onefuzz/pull/1332), [#1335](https://github.com/microsoft/onefuzz/pull/1335), [#1336](https://github.com/microsoft/onefuzz/pull/1336), [#1337](https://github.com/microsoft/onefuzz/pull/1337), [#1341](https://github.com/microsoft/onefuzz/pull/1341), [#1342](https://github.com/microsoft/onefuzz/pull/1342), [#1343](https://github.com/microsoft/onefuzz/pull/1343), [#1344](https://github.com/microsoft/onefuzz/pull/1344), [#1353](https://github.com/microsoft/onefuzz/pull/1353)
* CLI/Service: Updated multiple first-party and third-party Python dependencies.  [#1346](https://github.com/microsoft/onefuzz/pull/1346), [#1348](https://github.com/microsoft/onefuzz/pull/1348), [#1355](https://github.com/microsoft/onefuzz/pull/1355), [#1356](https://github.com/microsoft/onefuzz/pull/1356)

### Fixed

* Service: Fixed authentication when using a client secret. [#1300](https://github.com/microsoft/onefuzz/pull/1300)
* Deployment: Fixed an issue where the wrong AppRole was assigned when creating new CLI registrations. [#1308](https://github.com/microsoft/onefuzz/pull/1308)
* Deployment: Suppress a dependency's noisy logging of handled errors when deploying. [#1304](https://github.com/microsoft/onefuzz/pull/1304)

## 3.1.0

### Added

* Agent: Added ability to handle fake crash reports generated by debugging tools during regression tasks. [#1233](https://github.com/microsoft/onefuzz/pull/1233)
* Service: Added ability to configure virtual network IP ranges. [#1268](https://github.com/microsoft/onefuzz/pull/1268)
* Deployment: Added `flake8` to the deployment process to align with rest of the Python codebase linting. [#1286](https://github.com/microsoft/onefuzz/pull/1286)
* Service: Added custom extensions to enable Microsoft Security Monitoring extensions. [#1184](https://github.com/microsoft/onefuzz/pull/1184)
* CLI: Added `--readonly_inputs` option to the `libfuzzer basic` template. [#1247](https://github.com/microsoft/onefuzz/pull/1247)

### Changed

* CLI: Increased the default verbosity of destructive CLI commands. [#1264](https://github.com/microsoft/onefuzz/pull/1264)
* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies. [#1239](https://github.com/microsoft/onefuzz/pull/1239), [#1240](https://github.com/microsoft/onefuzz/pull/1240), [#1236](https://github.com/microsoft/onefuzz/pull/1236), [#1238](https://github.com/microsoft/onefuzz/pull/1238), [#1245](https://github.com/microsoft/onefuzz/pull/1245), [#1246](https://github.com/microsoft/onefuzz/pull/1246), [#1252](https://github.com/microsoft/onefuzz/pull/1252), [#1253](https://github.com/microsoft/onefuzz/pull/1253), [#1254](https://github.com/microsoft/onefuzz/pull/1254), [#1257](https://github.com/microsoft/onefuzz/pull/1257), [#1261](https://github.com/microsoft/onefuzz/pull/1261), [#1262](https://github.com/microsoft/onefuzz/pull/1262), [#1276](https://github.com/microsoft/onefuzz/pull/1276), [#1278](https://github.com/microsoft/onefuzz/pull/1278)

### Fixed

* Deployment: Fixed deployment in some regions by specifying widely-supported versions of Application Insights resources. [#1291](https://github.com/microsoft/onefuzz/pull/1291)
* Deployment: Fixed an issue with multi-tenant deployment caused by a mismatch between the identifier used to configure the app registration and value used to authenticate the CLI client. [#1270](https://github.com/microsoft/onefuzz/pull/1270)
* Service: Fixed `scaleset proxy reset` to reset all proxies in specified region. [#1275](https://github.com/microsoft/onefuzz/pull/1275)
* CLI: Temporarily ignore type errors from `azure-storage-blob` due to invalid Python type signatures. [#1258](https://github.com/microsoft/onefuzz/pull/1258)

## 3.0.0

### Changed

* CLI/Deployment/Service: Move to using `api://` for AAD Application "identifier URIs".
  Pre-3.0 clients will not be able to connect to newer instances.
  (BREAKING CHANGE) [#1243](https://github.com/microsoft/onefuzz/pull/1243)
* Agent/Supervisor/Proxy: Redact device, IP, and machine name in runtime statistics reported to Microsoft via Application Insights. [#1242](https://github.com/microsoft/onefuzz/pull/1242)
* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies.  [#1232](https://github.com/microsoft/onefuzz/pull/1232), [#1230](https://github.com/microsoft/onefuzz/pull/1230), [#1228](https://github.com/microsoft/onefuzz/pull/1228), [#1229](https://github.com/microsoft/onefuzz/pull/1229), [#1231](https://github.com/microsoft/onefuzz/pull/1231), [#1242](https://github.com/microsoft/onefuzz/pull/1242).

## 2.33.1

### Fixed

* CLI: Fixed an issue printing results that include `SecretData`. [#1223](https://github.com/microsoft/onefuzz/pull/1223)

## 2.33.0

### Added

* Agent: Added `machine_id` [configuration value expansion](docs/command-replacements.md) for all tasks.  [#1217](https://github.com/microsoft/onefuzz/pull/1217), [#1216](https://github.com/microsoft/onefuzz/pull/1216)

### Changed

* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies.  [#1215](https://github.com/microsoft/onefuzz/pull/1215), [#1214](https://github.com/microsoft/onefuzz/pull/1214), [#1213](https://github.com/microsoft/onefuzz/pull/1213), [#1211](https://github.com/microsoft/onefuzz/pull/1211), [#1218](https://github.com/microsoft/onefuzz/pull/1218), [#1219](https://github.com/microsoft/onefuzz/pull/1219)

### Fixed

* Deployment: Fixed the example deployment rule to include the required Azure Storage Queue support.  [#1207](https://github.com/microsoft/onefuzz/pull/1207)
* CLI: Fixed an issue printing results that include `set`, `datetime`, or `None`.  [#1208](https://github.com/microsoft/onefuzz/pull/1208), [#1221](https://github.com/microsoft/onefuzz/pull/1221)

## 2.32.0

### Added

* CLI/Service: The Azure VM SKU used for proxies is now configurable via `onefuzz instance_config`.  [#1128](https://github.com/microsoft/onefuzz/pull/1128)
* CLI: Added `onefuzz status pool` command to give status information for a pool.  [#1170](https://github.com/microsoft/onefuzz/pull/1170)

### Changed

* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies.  [#1152](https://github.com/microsoft/onefuzz/pull/1152), [#1155](https://github.com/microsoft/onefuzz/pull/1155), [#1156](https://github.com/microsoft/onefuzz/pull/1156), [#1157](https://github.com/microsoft/onefuzz/pull/1157), [#1158](https://github.com/microsoft/onefuzz/pull/1158), [#1163](https://github.com/microsoft/onefuzz/pull/1163), [#1164](https://github.com/microsoft/onefuzz/pull/1164), [#1165](https://github.com/microsoft/onefuzz/pull/1165), [#1166](https://github.com/microsoft/onefuzz/pull/1166), [#1176](https://github.com/microsoft/onefuzz/pull/1176), [#1177](https://github.com/microsoft/onefuzz/pull/1177), [#1178](https://github.com/microsoft/onefuzz/pull/1178), [#1179](https://github.com/microsoft/onefuzz/pull/1179), [#1181](https://github.com/microsoft/onefuzz/pull/1181), [#1182](https://github.com/microsoft/onefuzz/pull/1182), [#1183](https://github.com/microsoft/onefuzz/pull/1183), [#1185](https://github.com/microsoft/onefuzz/pull/1185), [#1186](https://github.com/microsoft/onefuzz/pull/1186), [#1191](https://github.com/microsoft/onefuzz/pull/1191), [#1198](https://github.com/microsoft/onefuzz/pull/1198), [#1199](https://github.com/microsoft/onefuzz/pull/1199), [#1200](https://github.com/microsoft/onefuzz/pull/1200), [#1201](https://github.com/microsoft/onefuzz/pull/1201), [#1202](https://github.com/microsoft/onefuzz/pull/1202), [#1203](https://github.com/microsoft/onefuzz/pull/1203), [#1204](https://github.com/microsoft/onefuzz/pull/1204), [#1205](https://github.com/microsoft/onefuzz/pull/1205)
* Agent: Changed `azcopy` calls to always retry when source files are modified mid-copy.  [#1196](https://github.com/microsoft/onefuzz/pull/1196)
* Agent: Continued development related to upcoming features.  [#1146](https://github.com/microsoft/onefuzz/pull/1146)
* Agent: SAS URLs are now redacted in logged `azcopy` failures.  [#1194](https://github.com/microsoft/onefuzz/pull/1194)
* CLI: Include the number of VMs used per-task in `onefuzz status top`.  [#1169](https://github.com/microsoft/onefuzz/pull/1169)
* Deployment: Application credentials created during deployment are no longer logged.  [#1172](https://github.com/microsoft/onefuzz/pull/1172)
* Deployment: Clarify logging when retrying AAD interactions.  [#1173](https://github.com/microsoft/onefuzz/pull/1173)
* Deployment: Replaced custom Azure Storage Queue creation with ARM templates.  [#1193](https://github.com/microsoft/onefuzz/pull/1193)
* Service: The validity period for SAS URLs is now back-dated to avoid time synchronization issues.  [#1195](https://github.com/microsoft/onefuzz/pull/1195)

### Fixed

* Deployment: Invalid preauthorized application references are removed during application registration.  [#1175](https://github.com/microsoft/onefuzz/pull/1175)
* Service: Fixed an issue logging node status.  [#1160](https://github.com/microsoft/onefuzz/pull/1160)

## 2.31.0

### Added

* Supervisor: Added recording of STDOUT and STDERR of the supervisor to file.  [#1109](https://github.com/microsoft/onefuzz/pull/1109)
* CLI/Service/Agent: Supervisor tasks can now optionally have a managed coverage container.  [#1123](https://github.com/microsoft/onefuzz/pull/1123)

### Changed

* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies.  [#1151](https://github.com/microsoft/onefuzz/pull/1151), [#1149](https://github.com/microsoft/onefuzz/pull/1149), [#1145](https://github.com/microsoft/onefuzz/pull/1145), [#1134](https://github.com/microsoft/onefuzz/pull/1134), [#1135](https://github.com/microsoft/onefuzz/pull/1135), [#1137](https://github.com/microsoft/onefuzz/pull/1137), [#1133](https://github.com/microsoft/onefuzz/pull/1133), [#1138](https://github.com/microsoft/onefuzz/pull/1138), [#1132](https://github.com/microsoft/onefuzz/pull/1132), [#1140](https://github.com/microsoft/onefuzz/pull/1140)
* Service: Enabled testing of the Azure Devops work item rendering.  [#1144](https://github.com/microsoft/onefuzz/pull/1144)
* Agent: Continued development related to upcoming features.  [#1142](https://github.com/microsoft/onefuzz/pull/1142)
* CLI: No longer retry service API requests that fail with service-level errors.  [#1129](https://github.com/microsoft/onefuzz/pull/1129)
* Agent/Supervisor/Proxy: Addressed multiple new `cargo-clippy` warnings.  [#1125](https://github.com/microsoft/onefuzz/pull/1125)
* CLI/Service: Updated third-party Python dependencies.  [#1124](https://github.com/microsoft/onefuzz/pull/1124)

### Fixed

* Service: Fixed an issue with incomplete authorization in multi-tenant deployments.  CVE-2021-37705 [#1153](https://github.com/microsoft/onefuzz/pull/1153)

## 2.30.0

### Changed

* Agent/Supervisor/Proxy: Updated multiple third-party Rust dependencies.  [#1116](https://github.com/microsoft/onefuzz/pull/1116)

### Fixed

* Service: Fixed an error when replacing notifications for a container. [#1115](https://github.com/microsoft/onefuzz/pull/1115)
* Service: Fixed Python 3.9 compatibility issues.  [#1117](https://github.com/microsoft/onefuzz/pull/1117)
* Agent/Supervisor/Proxy: Addressed multiple new `cargo-clippy` warnings.  [#1118](https://github.com/microsoft/onefuzz/pull/1118)

## 2.29.1

### Fixed

* Agent: Fixed an issue with the "Premium" storage account utilities.  [#1111](https://github.com/microsoft/onefuzz/pull/1111)
* Agent: Addressed a rate-limiting issue when using `azcopy` from a large number of VMs with numbers cores.  [#1112](https://github.com/microsoft/onefuzz/pull/1112)

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
* Service: Updated multiple first-party and third-party Python libraries.  [#893](https://github.com/microsoft/onefuzz/pull/893), [#889](https://github.com/microsoft/onefuzz/pull/889), [#866](https://github.com/microsoft/onefuzz/pull/886), [#885](https://github.com/microsoft/onefuzz/pull/885), [#861](https://github.com/microsoft/onefuzz/pull/861), [#890](https://github.com/microsoft/onefuzz/pull/890)
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

* Agent/Proxy/Supervisor : Updated multiple third-party Rust dependencies.  [#842](https://github.com/microsoft/onefuzz/pull/842), [#826](https://github.com/microsoft/onefuzz/pull/826), [#829](https://github.com/microsoft/onefuzz/pull/829)
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

* Deployment: Addressed an issue with nested exceptions triggered during a failed deployment [#172](https://github.com/microsoft/onefuzz/pull/172)
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
