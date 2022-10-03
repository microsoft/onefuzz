#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import os
import tempfile
from enum import Enum
from typing import Dict, List, Optional

from onefuzztypes.enums import OS, ContainerType, TaskDebugFlag, TaskType
from onefuzztypes.models import Job, NotificationConfig
from onefuzztypes.primitives import Container, Directory, File, PoolName

from onefuzz.api import Command

from . import JobHelper

LIBFUZZER_MAGIC_STRING = b"ERROR: libFuzzer"

# These path constants point to identically-named managed DLLs, so it may seem like one
# of them is redundant. This is false: the DLLs are _not_ identical, and each one links
# platform-native code.
#
# The reason for this is that `libfuzzer-dotnet` needs a _platform-native_ executable
# wrapper to invoke the managed code under test as a child process, then communicate with
# it using OS-specific IPC. As a result, we must deploy two variants of the managed DLL on
# VMs, along with other platform-native executables to support the fuzzing task.
#
# The `dotnet_coverage` and `dotnet_crash_report` tasks must invoke the underlying DLL, so
# it must not be statically linked into the platform-native executable wrapper.
LIBFUZZER_DOTNET_LOADER_PATH_LINUX = "/onefuzz/third-party/dotnet-fuzzing-linux/LibFuzzerDotnetLoader/LibFuzzerDotnetLoader.dll"
LIBFUZZER_DOTNET_LOADER_PATH_WINDOWS = "/onefuzz/third-party/dotnet-fuzzing-windows/LibFuzzerDotnetLoader/LibFuzzerDotnetLoader.dll"


class QemuArch(Enum):
    aarch64 = "aarch64"


class Libfuzzer(Command):
    """Pre-defined Libfuzzer job"""

    def _check_is_libfuzzer(self, target_exe: File) -> None:
        """Look for a magic string"""
        self.logger.debug(
            "checking %s for %s", repr(target_exe), repr(LIBFUZZER_MAGIC_STRING)
        )
        with open(target_exe, "rb") as handle:
            data = handle.read()

        if LIBFUZZER_MAGIC_STRING not in data:
            raise Exception("not a libfuzzer binary: %s" % target_exe)

    def _create_tasks(
        self,
        *,
        job: Job,
        containers: Dict[ContainerType, Container],
        pool_name: PoolName,
        target_exe: str,
        vm_count: int = 2,
        reboot_after_setup: bool = False,
        duration: int = 24,
        target_workers: Optional[int] = None,
        target_options: Optional[List[str]] = None,
        fuzzing_target_options: Optional[List[str]] = None,
        target_env: Optional[Dict[str, str]] = None,
        target_timeout: Optional[int] = None,
        tags: Optional[Dict[str, str]] = None,
        check_retry_count: Optional[int] = None,
        crash_report_timeout: Optional[int] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        ensemble_sync_delay: Optional[int] = None,
        colocate_all_tasks: bool = False,
        colocate_secondary_tasks: bool = True,
        check_fuzzer_help: bool = True,
        expect_crash_on_failure: bool = False,
        minimized_stack_depth: Optional[int] = None,
        coverage_filter: Optional[str] = None,
    ) -> None:
        target_options = target_options or []

        regression_containers = [
            (ContainerType.setup, containers[ContainerType.setup]),
            (ContainerType.crashes, containers[ContainerType.crashes]),
            (ContainerType.unique_reports, containers[ContainerType.unique_reports]),
            (
                ContainerType.regression_reports,
                containers[ContainerType.regression_reports],
            ),
        ]

        # We don't really need a separate timeout for crash reporting, and we could just
        # use `target_timeout`. But `crash_report_timeout` was introduced first, so we
        # can't remove it without a breaking change. Since both timeouts may be present,
        # prefer the more task-specific timeout.
        effective_crash_report_timeout = crash_report_timeout or target_timeout

        self.logger.info("creating libfuzzer_regression task")
        regression_task = self.onefuzz.tasks.create(
            job.job_id,
            TaskType.libfuzzer_regression,
            target_exe,
            regression_containers,
            pool_name=pool_name,
            duration=duration,
            vm_count=1,
            reboot_after_setup=reboot_after_setup,
            target_options=target_options,
            target_env=target_env,
            tags=tags,
            target_timeout=effective_crash_report_timeout,
            check_retry_count=check_retry_count,
            check_fuzzer_help=check_fuzzer_help,
            debug=debug,
            colocate=colocate_all_tasks or colocate_secondary_tasks,
            minimized_stack_depth=minimized_stack_depth,
        )

        fuzzer_containers = [
            (ContainerType.setup, containers[ContainerType.setup]),
            (ContainerType.crashes, containers[ContainerType.crashes]),
            (ContainerType.inputs, containers[ContainerType.inputs]),
        ]

        if ContainerType.readonly_inputs in containers:
            fuzzer_containers.append(
                (
                    ContainerType.readonly_inputs,
                    containers[ContainerType.readonly_inputs],
                )
            )

        self.logger.info("creating libfuzzer task")

        # disable ensemble sync if only one VM is used
        if ensemble_sync_delay is None and vm_count == 1:
            ensemble_sync_delay = 0

        # Build `target_options` for the `libfuzzer_fuzz` task.
        #
        # This allows passing arguments like `-runs` to the target only when
        # invoked in persistent fuzzing mode, and not test case repro mode.
        libfuzzer_fuzz_target_options = target_options.copy()

        if fuzzing_target_options:
            libfuzzer_fuzz_target_options += fuzzing_target_options

        fuzzer_task = self.onefuzz.tasks.create(
            job.job_id,
            TaskType.libfuzzer_fuzz,
            target_exe,
            fuzzer_containers,
            pool_name=pool_name,
            reboot_after_setup=reboot_after_setup,
            duration=duration,
            vm_count=vm_count,
            target_options=libfuzzer_fuzz_target_options,
            target_env=target_env,
            target_workers=target_workers,
            tags=tags,
            debug=debug,
            ensemble_sync_delay=ensemble_sync_delay,
            colocate=colocate_all_tasks,
            check_fuzzer_help=check_fuzzer_help,
            expect_crash_on_failure=expect_crash_on_failure,
        )

        prereq_tasks = [fuzzer_task.task_id, regression_task.task_id]

        coverage_containers = [
            (ContainerType.setup, containers[ContainerType.setup]),
            (ContainerType.coverage, containers[ContainerType.coverage]),
            (ContainerType.readonly_inputs, containers[ContainerType.inputs]),
        ]

        if ContainerType.readonly_inputs in containers:
            coverage_containers.append(
                (
                    ContainerType.readonly_inputs,
                    containers[ContainerType.readonly_inputs],
                )
            )

        self.logger.info("creating coverage task")

        # The `coverage` task is not libFuzzer-aware, so invocations of the target fuzzer
        # against an input do not automatically add an `{input}` specifier to the command
        # args. That means on the VM, the fuzzer will get run in fuzzing mode each time we
        # try to test an input.
        #
        # We cannot require `{input}` occur in `target_options`, since that would break
        # the current assumptions of the libFuzzer-aware tasks, as well as be a breaking
        # API change.
        #
        # For now, locally extend the `target_options` for this task only, to ensure that
        # test case invocations work as expected.
        coverage_target_options = target_options.copy() if target_options else []
        coverage_target_options.append("{input}")

        # Opposite precedence to `effective_crash_report_timeout`.
        #
        # If the user specified a timeout for crash reporting but not a general target
        # timeout, consider that to be a better (more target-aware) default than the
        # default in the agent.
        coverage_timeout = target_timeout or crash_report_timeout

        self.onefuzz.tasks.create(
            job.job_id,
            TaskType.coverage,
            target_exe,
            coverage_containers,
            pool_name=pool_name,
            duration=duration,
            vm_count=1,
            reboot_after_setup=reboot_after_setup,
            target_options=coverage_target_options,
            target_env=target_env,
            target_timeout=coverage_timeout,
            tags=tags,
            prereq_tasks=prereq_tasks,
            debug=debug,
            colocate=colocate_all_tasks or colocate_secondary_tasks,
            check_fuzzer_help=check_fuzzer_help,
            coverage_filter=coverage_filter,
        )

        report_containers = [
            (ContainerType.setup, containers[ContainerType.setup]),
            (ContainerType.crashes, containers[ContainerType.crashes]),
            (ContainerType.reports, containers[ContainerType.reports]),
            (ContainerType.unique_reports, containers[ContainerType.unique_reports]),
            (ContainerType.no_repro, containers[ContainerType.no_repro]),
        ]

        self.logger.info("creating libfuzzer_crash_report task")
        self.onefuzz.tasks.create(
            job.job_id,
            TaskType.libfuzzer_crash_report,
            target_exe,
            report_containers,
            pool_name=pool_name,
            duration=duration,
            vm_count=1,
            reboot_after_setup=reboot_after_setup,
            target_options=target_options,
            target_env=target_env,
            tags=tags,
            prereq_tasks=prereq_tasks,
            target_timeout=effective_crash_report_timeout,
            check_retry_count=check_retry_count,
            check_fuzzer_help=check_fuzzer_help,
            debug=debug,
            colocate=colocate_all_tasks or colocate_secondary_tasks,
            minimized_stack_depth=minimized_stack_depth,
        )

    def basic(
        self,
        project: str,
        name: str,
        build: str,
        pool_name: PoolName,
        *,
        target_exe: File = File("fuzz.exe"),
        setup_dir: Optional[Directory] = None,
        vm_count: int = 2,
        inputs: Optional[Directory] = None,
        reboot_after_setup: bool = False,
        duration: int = 24,
        target_workers: Optional[int] = None,
        target_options: Optional[List[str]] = None,
        fuzzing_target_options: Optional[List[str]] = None,
        target_env: Optional[Dict[str, str]] = None,
        target_timeout: Optional[int] = None,
        check_retry_count: Optional[int] = None,
        crash_report_timeout: Optional[int] = None,
        tags: Optional[Dict[str, str]] = None,
        wait_for_running: bool = False,
        wait_for_files: Optional[List[ContainerType]] = None,
        extra_files: Optional[List[File]] = None,
        existing_inputs: Optional[Container] = None,
        readonly_inputs: Optional[Container] = None,
        dryrun: bool = False,
        notification_config: Optional[NotificationConfig] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        ensemble_sync_delay: Optional[int] = None,
        colocate_all_tasks: bool = False,
        colocate_secondary_tasks: bool = True,
        check_fuzzer_help: bool = True,
        expect_crash_on_failure: bool = False,
        minimized_stack_depth: Optional[int] = None,
        coverage_filter: Optional[File] = None,
    ) -> Optional[Job]:
        """
        Basic libfuzzer job

        :param bool ensemble_sync_delay: Specify duration between
            syncing inputs during ensemble fuzzing (0 to disable).
        """

        # verify containers exist
        if existing_inputs:
            self.onefuzz.containers.get(existing_inputs)

        if readonly_inputs:
            self.onefuzz.containers.get(readonly_inputs)

        if dryrun:
            return None

        self.logger.info("creating libfuzzer from template")

        self._check_is_libfuzzer(target_exe)

        helper = JobHelper(
            self.onefuzz,
            self.logger,
            project,
            name,
            build,
            duration,
            pool_name=pool_name,
            target_exe=target_exe,
        )

        helper.add_tags(tags)
        helper.define_containers(
            ContainerType.setup,
            ContainerType.inputs,
            ContainerType.crashes,
            ContainerType.reports,
            ContainerType.unique_reports,
            ContainerType.unique_inputs,
            ContainerType.no_repro,
            ContainerType.coverage,
            ContainerType.unique_inputs,
            ContainerType.regression_reports,
        )

        if existing_inputs:
            self.onefuzz.containers.get(existing_inputs)
            helper.containers[ContainerType.inputs] = existing_inputs
        else:
            helper.define_containers(ContainerType.inputs)

        if readonly_inputs:
            self.onefuzz.containers.get(readonly_inputs)
            helper.containers[ContainerType.readonly_inputs] = readonly_inputs

        helper.create_containers()
        helper.setup_notifications(notification_config)

        helper.upload_setup(setup_dir, target_exe, extra_files)
        if inputs:
            helper.upload_inputs(inputs)
        helper.wait_on(wait_for_files, wait_for_running)

        target_exe_blob_name = helper.setup_relative_blob_name(target_exe, setup_dir)

        if coverage_filter:
            coverage_filter_blob_name: Optional[str] = helper.setup_relative_blob_name(
                coverage_filter, setup_dir
            )
        else:
            coverage_filter_blob_name = None

        self._create_tasks(
            job=helper.job,
            containers=helper.containers,
            pool_name=pool_name,
            target_exe=target_exe_blob_name,
            vm_count=vm_count,
            reboot_after_setup=reboot_after_setup,
            duration=duration,
            target_workers=target_workers,
            target_options=target_options,
            fuzzing_target_options=fuzzing_target_options,
            target_env=target_env,
            tags=helper.tags,
            crash_report_timeout=crash_report_timeout,
            check_retry_count=check_retry_count,
            debug=debug,
            ensemble_sync_delay=ensemble_sync_delay,
            colocate_all_tasks=colocate_all_tasks,
            colocate_secondary_tasks=colocate_secondary_tasks,
            check_fuzzer_help=check_fuzzer_help,
            expect_crash_on_failure=expect_crash_on_failure,
            minimized_stack_depth=minimized_stack_depth,
            coverage_filter=coverage_filter_blob_name,
        )

        self.logger.info("done creating tasks")
        helper.wait()
        return helper.job

    def merge(
        self,
        project: str,
        name: str,
        build: str,
        pool_name: PoolName,
        *,
        target_exe: File = File("fuzz.exe"),
        setup_dir: Optional[Directory] = None,
        inputs: Optional[Directory] = None,
        output_container: Optional[Container] = None,
        reboot_after_setup: bool = False,
        duration: int = 24,
        target_options: Optional[List[str]] = None,
        target_env: Optional[Dict[str, str]] = None,
        check_retry_count: Optional[int] = None,
        crash_report_timeout: Optional[int] = None,
        tags: Optional[Dict[str, str]] = None,
        wait_for_running: bool = False,
        wait_for_files: Optional[List[ContainerType]] = None,
        extra_files: Optional[List[File]] = None,
        existing_inputs: Optional[List[Container]] = None,
        dryrun: bool = False,
        notification_config: Optional[NotificationConfig] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        preserve_existing_outputs: bool = False,
        check_fuzzer_help: bool = True,
    ) -> Optional[Job]:

        """
        libfuzzer merge task
        """

        # verify containers exist
        if existing_inputs:
            for existing_container in existing_inputs:
                self.onefuzz.containers.get(existing_container)
        elif not inputs:
            self.logger.error(
                "please specify either an input folder or at least one existing inputs container"
            )
            return None

        if dryrun:
            return None

        self.logger.info("creating libfuzzer merge from template")
        self._check_is_libfuzzer(target_exe)

        helper = JobHelper(
            self.onefuzz,
            self.logger,
            project,
            name,
            build,
            duration,
            pool_name=pool_name,
            target_exe=target_exe,
        )

        helper.add_tags(tags)
        helper.define_containers(
            ContainerType.setup,
        )
        if inputs:
            helper.define_containers(ContainerType.inputs)

        if output_container:
            if self.onefuzz.containers.get(output_container):
                helper.define_containers(ContainerType.unique_inputs)

        helper.create_containers()
        helper.setup_notifications(notification_config)

        helper.upload_setup(setup_dir, target_exe, extra_files)
        if inputs:
            helper.upload_inputs(inputs)
        helper.wait_on(wait_for_files, wait_for_running)

        target_exe_blob_name = helper.setup_relative_blob_name(target_exe, setup_dir)

        merge_containers = [
            (ContainerType.setup, helper.containers[ContainerType.setup]),
            (
                ContainerType.unique_inputs,
                output_container or helper.containers[ContainerType.unique_inputs],
            ),
        ]

        if inputs:
            merge_containers.append(
                (ContainerType.inputs, helper.containers[ContainerType.inputs])
            )
        if existing_inputs:
            for existing_container in existing_inputs:
                merge_containers.append((ContainerType.inputs, existing_container))

        self.logger.info("creating libfuzzer_merge task")
        self.onefuzz.tasks.create(
            helper.job.job_id,
            TaskType.libfuzzer_merge,
            target_exe_blob_name,
            merge_containers,
            pool_name=pool_name,
            duration=duration,
            vm_count=1,
            reboot_after_setup=reboot_after_setup,
            target_options=target_options,
            target_env=target_env,
            tags=tags,
            target_timeout=crash_report_timeout,
            check_retry_count=check_retry_count,
            debug=debug,
            preserve_existing_outputs=preserve_existing_outputs,
            check_fuzzer_help=check_fuzzer_help,
        )

        self.logger.info("done creating tasks")
        helper.wait()
        return helper.job

    def dotnet(
        self,
        project: str,
        name: str,
        build: str,
        pool_name: PoolName,
        *,
        setup_dir: Directory,
        target_harness: str,
        vm_count: int = 1,
        inputs: Optional[Directory] = None,
        reboot_after_setup: bool = False,
        duration: int = 24,
        target_workers: Optional[int] = None,
        target_options: Optional[List[str]] = None,
        fuzzing_target_options: Optional[List[str]] = None,
        target_env: Optional[Dict[str, str]] = None,
        tags: Optional[Dict[str, str]] = None,
        wait_for_running: bool = False,
        wait_for_files: Optional[List[ContainerType]] = None,
        existing_inputs: Optional[Container] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        ensemble_sync_delay: Optional[int] = None,
        check_fuzzer_help: bool = True,
        expect_crash_on_failure: bool = False,
    ) -> Optional[Job]:

        """
        libfuzzer-dotnet task
        """

        harness = "libfuzzer-dotnet"

        pool = self.onefuzz.pools.get(pool_name)
        if pool.os != OS.linux:
            raise Exception("libfuzzer-dotnet jobs are only compatible on linux")

        target_exe = File(os.path.join(setup_dir, harness))
        if not os.path.exists(target_exe):
            raise Exception(f"missing harness: {target_exe}")

        assembly_path = os.path.join(setup_dir, target_harness)
        if not os.path.exists(assembly_path):
            raise Exception(f"missing assembly: {target_harness}")

        self._check_is_libfuzzer(target_exe)
        if target_options is None:
            target_options = []
        target_options = [
            "--target_path={setup_dir}/" + "{target_harness}"
        ] + target_options

        helper = JobHelper(
            self.onefuzz,
            self.logger,
            project,
            name,
            build,
            duration,
            pool_name=pool_name,
            target_exe=target_exe,
        )

        helper.add_tags(tags)
        helper.define_containers(
            ContainerType.setup,
            ContainerType.inputs,
            ContainerType.crashes,
        )

        if existing_inputs:
            self.onefuzz.containers.get(existing_inputs)
            helper.containers[ContainerType.inputs] = existing_inputs
        else:
            helper.define_containers(ContainerType.inputs)

        fuzzer_containers = [
            (ContainerType.setup, helper.containers[ContainerType.setup]),
            (ContainerType.crashes, helper.containers[ContainerType.crashes]),
            (ContainerType.inputs, helper.containers[ContainerType.inputs]),
        ]

        helper.create_containers()

        helper.upload_setup(setup_dir, target_exe)
        if inputs:
            helper.upload_inputs(inputs)
        helper.wait_on(wait_for_files, wait_for_running)

        # Build `target_options` for the `libfuzzer_fuzz` task.
        #
        # This allows passing arguments like `-runs` to the target only when
        # invoked in persistent fuzzing mode, and not test case repro mode.
        libfuzzer_fuzz_target_options = target_options.copy()

        if fuzzing_target_options:
            libfuzzer_fuzz_target_options += fuzzing_target_options

        self.onefuzz.tasks.create(
            helper.job.job_id,
            TaskType.libfuzzer_fuzz,
            harness,
            fuzzer_containers,
            pool_name=pool_name,
            reboot_after_setup=reboot_after_setup,
            duration=duration,
            vm_count=vm_count,
            target_options=libfuzzer_fuzz_target_options,
            target_env=target_env,
            target_workers=target_workers,
            tags=tags,
            debug=debug,
            ensemble_sync_delay=ensemble_sync_delay,
            check_fuzzer_help=check_fuzzer_help,
            expect_crash_on_failure=expect_crash_on_failure,
        )

        self.logger.info("done creating tasks")
        helper.wait()
        return helper.job

    def dotnet_dll(
        self,
        project: str,
        name: str,
        build: str,
        pool_name: PoolName,
        *,
        setup_dir: Directory,
        target_dll: File,
        target_class: str,
        target_method: str,
        vm_count: int = 1,
        inputs: Optional[Directory] = None,
        reboot_after_setup: bool = False,
        duration: int = 24,
        target_workers: Optional[int] = None,
        fuzzing_target_options: Optional[List[str]] = None,
        target_env: Optional[Dict[str, str]] = None,
        target_timeout: Optional[int] = None,
        check_retry_count: Optional[int] = None,
        tags: Optional[Dict[str, str]] = None,
        wait_for_running: bool = False,
        wait_for_files: Optional[List[ContainerType]] = None,
        existing_inputs: Optional[Container] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        ensemble_sync_delay: Optional[int] = None,
        colocate_all_tasks: bool = False,
        colocate_secondary_tasks: bool = True,
        expect_crash_on_failure: bool = False,
    ) -> Optional[Job]:
        pool = self.onefuzz.pools.get(pool_name)

        # We _must_ proactively specify the OS based on pool.
        #
        # This is because managed DLLs are always (Windows-native) PE files, so the job
        # helper's platform guess (based on the file type of `target_exe`) will always
        # evaluate to `OS.windows`. In the case of true Linux `libfuzzer dotnet_dll` jobs,
        # this leads to a client- side validation error when the helper checks the nominal
        # target OS against the pool OS.
        platform = pool.os

        helper = JobHelper(
            self.onefuzz,
            self.logger,
            project,
            name,
            build,
            duration,
            pool_name=pool_name,
            target_exe=target_dll,
            platform=platform,
        )

        target_env = target_env or {}

        # Set target environment variables for `LibFuzzerDotnetLoader`.
        target_env["LIBFUZZER_DOTNET_TARGET_ASSEMBLY"] = target_dll
        target_env["LIBFUZZER_DOTNET_TARGET_CLASS"] = target_class
        target_env["LIBFUZZER_DOTNET_TARGET_METHOD"] = target_method

        helper.add_tags(tags)
        helper.define_containers(
            ContainerType.setup,
            ContainerType.inputs,
            ContainerType.crashes,
        )

        containers = helper.containers

        if existing_inputs:
            self.onefuzz.containers.get(existing_inputs)
            helper.containers[ContainerType.inputs] = existing_inputs
        else:
            helper.define_containers(ContainerType.inputs)

        fuzzer_containers = [
            (ContainerType.setup, containers[ContainerType.setup]),
            (ContainerType.crashes, containers[ContainerType.crashes]),
            (ContainerType.inputs, containers[ContainerType.inputs]),
        ]

        helper.create_containers()

        helper.upload_setup(setup_dir, target_dll)

        if inputs:
            helper.upload_inputs(inputs)

        helper.wait_on(wait_for_files, wait_for_running)

        fuzzer_task = self.onefuzz.tasks.create(
            helper.job.job_id,
            TaskType.libfuzzer_dotnet_fuzz,
            target_dll,
            fuzzer_containers,
            pool_name=pool_name,
            reboot_after_setup=reboot_after_setup,
            duration=duration,
            vm_count=vm_count,
            target_options=fuzzing_target_options,
            target_env=target_env,
            target_workers=target_workers,
            tags=tags,
            debug=debug,
            ensemble_sync_delay=ensemble_sync_delay,
            expect_crash_on_failure=expect_crash_on_failure,
        )

        # Ensure the fuzzing task starts before we schedule the coverage and
        # crash reporting tasks (which are useless without it).
        prereq_tasks = [fuzzer_task.task_id]

        # Target options for the .NET harness produced by SharpFuzz, when _not_
        # invoked as a child process of `libfuzzer-dotnet`. This harness has a
        # `main()` function with one argument: the path to an input test case.
        sharpfuzz_harness_target_options = ["{input}"]

        # Set the path to the `LibFuzzerDotnetLoader` DLL.
        #
        # This provides a `main()` function that dynamically loads a target DLL
        # passed via environment variables. This is assumed to be installed on
        # the VMs.
        if pool.os == OS.linux:
            libfuzzer_dotnet_loader_dll = LIBFUZZER_DOTNET_LOADER_PATH_LINUX
        elif pool.os == OS.windows:
            libfuzzer_dotnet_loader_dll = LIBFUZZER_DOTNET_LOADER_PATH_WINDOWS
        else:
            raise Exception("libfuzzer-dotnet jobs must run on Windows or Linux hosts")

        coverage_containers = [
            (ContainerType.setup, containers[ContainerType.setup]),
            (ContainerType.coverage, containers[ContainerType.coverage]),
            (ContainerType.readonly_inputs, containers[ContainerType.inputs]),
        ]

        self.logger.info("creating `dotnet_coverage` task")
        self.onefuzz.tasks.create(
            helper.job.job_id,
            TaskType.dotnet_coverage,
            libfuzzer_dotnet_loader_dll,
            coverage_containers,
            pool_name=pool_name,
            duration=duration,
            vm_count=1,
            reboot_after_setup=reboot_after_setup,
            target_options=sharpfuzz_harness_target_options,
            target_env=target_env,
            target_timeout=target_timeout,
            tags=tags,
            prereq_tasks=prereq_tasks,
            debug=debug,
            colocate=colocate_all_tasks or colocate_secondary_tasks,
        )

        report_containers = [
            (ContainerType.setup, containers[ContainerType.setup]),
            (ContainerType.crashes, containers[ContainerType.crashes]),
            (ContainerType.reports, containers[ContainerType.reports]),
            (ContainerType.unique_reports, containers[ContainerType.unique_reports]),
            (ContainerType.no_repro, containers[ContainerType.no_repro]),
        ]

        self.logger.info("creating `dotnet_crash_report` task")
        self.onefuzz.tasks.create(
            helper.job.job_id,
            TaskType.dotnet_crash_report,
            libfuzzer_dotnet_loader_dll,
            report_containers,
            pool_name=pool_name,
            duration=duration,
            vm_count=1,
            reboot_after_setup=reboot_after_setup,
            target_options=sharpfuzz_harness_target_options,
            target_env=target_env,
            tags=tags,
            prereq_tasks=prereq_tasks,
            target_timeout=target_timeout,
            check_retry_count=check_retry_count,
            debug=debug,
            colocate=colocate_all_tasks or colocate_secondary_tasks,
        )

        self.logger.info("done creating tasks")
        helper.wait()
        return helper.job

    def qemu_user(
        self,
        project: str,
        name: str,
        build: str,
        pool_name: PoolName,
        *,
        arch: QemuArch = QemuArch.aarch64,
        target_exe: File = File("fuzz.exe"),
        sysroot: Optional[File] = None,
        vm_count: int = 1,
        inputs: Optional[Directory] = None,
        reboot_after_setup: bool = False,
        duration: int = 24,
        target_workers: Optional[int] = 1,
        target_options: Optional[List[str]] = None,
        fuzzing_target_options: Optional[List[str]] = None,
        target_env: Optional[Dict[str, str]] = None,
        tags: Optional[Dict[str, str]] = None,
        wait_for_running: bool = False,
        wait_for_files: Optional[List[ContainerType]] = None,
        existing_inputs: Optional[Container] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        ensemble_sync_delay: Optional[int] = None,
        colocate_all_tasks: bool = False,
        crash_report_timeout: Optional[int] = 1,
        check_retry_count: Optional[int] = 300,
        check_fuzzer_help: bool = True,
    ) -> Optional[Job]:

        """
        libfuzzer tasks, wrapped via qemu-user (PREVIEW FEATURE)
        """

        self.logger.warning(
            "qemu_user jobs are a preview feature and may change in the future"
        )

        pool = self.onefuzz.pools.get(pool_name)
        if pool.os != OS.linux:
            raise Exception("libfuzzer qemu-user jobs are only compatible with Linux")

        self._check_is_libfuzzer(target_exe)

        if target_options is None:
            target_options = []

        # disable detect_leaks, as this is non-functional on cross-compile targets
        if target_env is None:
            target_env = {}
        target_env["ASAN_OPTIONS"] = (
            target_env.get("ASAN_OPTIONS", "") + ":detect_leaks=0"
        )

        helper = JobHelper(
            self.onefuzz,
            self.logger,
            project,
            name,
            build,
            duration,
            pool_name=pool_name,
            target_exe=target_exe,
        )

        helper.add_tags(tags)
        helper.define_containers(
            ContainerType.setup,
            ContainerType.inputs,
            ContainerType.crashes,
            ContainerType.reports,
            ContainerType.unique_reports,
            ContainerType.no_repro,
        )

        if existing_inputs:
            self.onefuzz.containers.get(existing_inputs)
            helper.containers[ContainerType.inputs] = existing_inputs
        else:
            helper.define_containers(ContainerType.inputs)

        fuzzer_containers = [
            (ContainerType.setup, helper.containers[ContainerType.setup]),
            (ContainerType.crashes, helper.containers[ContainerType.crashes]),
            (ContainerType.inputs, helper.containers[ContainerType.inputs]),
        ]

        helper.create_containers()

        target_exe_blob_name = helper.setup_relative_blob_name(target_exe, None)

        wrapper_name = File(target_exe_blob_name + "-wrapper.sh")

        with tempfile.TemporaryDirectory() as tempdir:
            if sysroot:
                setup_path = File(os.path.join(tempdir, "setup.sh"))
                with open(setup_path, "w", newline="\n") as handle:
                    sysroot_filename = helper.setup_relative_blob_name(sysroot, None)
                    handle.write(
                        "#!/bin/bash\n"
                        "set -ex\n"
                        "sudo apt-get install -y qemu-user g++-aarch64-linux-gnu libasan5-arm64-cross\n"
                        'cd $(dirname "$(readlink -f "$0")")\n'
                        "mkdir -p sysroot\n"
                        "tar -C sysroot -zxvf %s\n" % sysroot_filename
                    )

                wrapper_path = File(os.path.join(tempdir, wrapper_name))
                with open(wrapper_path, "w", newline="\n") as handle:
                    handle.write(
                        "#!/bin/bash\n"
                        'SETUP_DIR=$(dirname "$(readlink -f "$0")")\n'
                        "qemu-%s -L $SETUP_DIR/sysroot $SETUP_DIR/%s $*"
                        % (arch.name, target_exe_blob_name)
                    )
                upload_files = [setup_path, wrapper_path, sysroot]
            else:
                setup_path = File(os.path.join(tempdir, "setup.sh"))
                with open(setup_path, "w", newline="\n") as handle:
                    handle.write(
                        "#!/bin/bash\n"
                        "set -ex\n"
                        "sudo apt-get install -y qemu-user g++-aarch64-linux-gnu libasan5-arm64-cross\n"
                    )

                wrapper_path = File(os.path.join(tempdir, wrapper_name))
                with open(wrapper_path, "w", newline="\n") as handle:
                    handle.write(
                        "#!/bin/bash\n"
                        'SETUP_DIR=$(dirname "$(readlink -f "$0")")\n'
                        "qemu-%s -L /usr/%s-linux-gnu $SETUP_DIR/%s $*"
                        % (arch.name, arch.name, target_exe_blob_name)
                    )
                upload_files = [setup_path, wrapper_path]
            helper.upload_setup(None, target_exe, upload_files)

        if inputs:
            helper.upload_inputs(inputs)
        helper.wait_on(wait_for_files, wait_for_running)

        # Build `target_options` for the `libfuzzer_fuzz` task.
        #
        # This allows passing arguments like `-runs` to the target only when
        # invoked in persistent fuzzing mode, and not test case repro mode.
        libfuzzer_fuzz_target_options = target_options.copy()

        if fuzzing_target_options:
            libfuzzer_fuzz_target_options += fuzzing_target_options

        self.logger.info("creating libfuzzer_fuzz task")
        fuzzer_task = self.onefuzz.tasks.create(
            helper.job.job_id,
            TaskType.libfuzzer_fuzz,
            wrapper_name,
            fuzzer_containers,
            pool_name=pool_name,
            reboot_after_setup=reboot_after_setup,
            duration=duration,
            vm_count=vm_count,
            target_options=libfuzzer_fuzz_target_options,
            target_env=target_env,
            target_workers=target_workers,
            tags=tags,
            debug=debug,
            ensemble_sync_delay=ensemble_sync_delay,
            expect_crash_on_failure=False,
            check_fuzzer_help=check_fuzzer_help,
        )

        report_containers = [
            (ContainerType.setup, helper.containers[ContainerType.setup]),
            (ContainerType.crashes, helper.containers[ContainerType.crashes]),
            (ContainerType.reports, helper.containers[ContainerType.reports]),
            (
                ContainerType.unique_reports,
                helper.containers[ContainerType.unique_reports],
            ),
            (ContainerType.no_repro, helper.containers[ContainerType.no_repro]),
        ]

        self.logger.info("creating libfuzzer_crash_report task")
        self.onefuzz.tasks.create(
            helper.job.job_id,
            TaskType.libfuzzer_crash_report,
            wrapper_name,
            report_containers,
            pool_name=pool_name,
            duration=duration,
            vm_count=1,
            reboot_after_setup=reboot_after_setup,
            target_options=target_options,
            target_env=target_env,
            tags=tags,
            prereq_tasks=[fuzzer_task.task_id],
            target_timeout=crash_report_timeout,
            check_retry_count=check_retry_count,
            debug=debug,
            colocate=colocate_all_tasks,
            expect_crash_on_failure=False,
            check_fuzzer_help=check_fuzzer_help,
        )

        self.logger.info("done creating tasks")
        helper.wait()
        return helper.job
