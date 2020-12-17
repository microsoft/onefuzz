#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Dict, List, Optional

from onefuzztypes.enums import ContainerType, TaskDebugFlag, TaskType
from onefuzztypes.models import Job, NotificationConfig
from onefuzztypes.primitives import Container, Directory, File

from onefuzz.api import Command

from . import JobHelper

LIBFUZZER_MAGIC_STRING = b"ERROR: libFuzzer"


class Libfuzzer(Command):
    """ Pre-defined Libfuzzer job """

    def _check_is_libfuzzer(self, target_exe: File) -> None:
        """ Look for a magic string """
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
        pool_name: str,
        target_exe: str,
        vm_count: int = 2,
        reboot_after_setup: bool = False,
        duration: int = 24,
        target_workers: Optional[int] = None,
        target_options: Optional[List[str]] = None,
        target_env: Optional[Dict[str, str]] = None,
        tags: Optional[Dict[str, str]] = None,
        check_retry_count: Optional[int] = None,
        crash_report_timeout: Optional[int] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        ensemble_sync_delay: Optional[int] = None,
        colocate_all_tasks: bool = False,
        colocate_secondary_tasks: bool = True,
    ) -> None:

        fuzzer_containers = [
            (ContainerType.setup, containers[ContainerType.setup]),
            (ContainerType.crashes, containers[ContainerType.crashes]),
            (ContainerType.inputs, containers[ContainerType.inputs]),
        ]
        self.logger.info("creating libfuzzer task")

        # disable ensemble sync if only one VM is used
        if ensemble_sync_delay is None and vm_count == 1:
            ensemble_sync_delay = 0

        fuzzer_task = self.onefuzz.tasks.create(
            job.job_id,
            TaskType.libfuzzer_fuzz,
            target_exe,
            fuzzer_containers,
            pool_name=pool_name,
            reboot_after_setup=reboot_after_setup,
            duration=duration,
            vm_count=vm_count,
            target_options=target_options,
            target_env=target_env,
            target_workers=target_workers,
            tags=tags,
            debug=debug,
            ensemble_sync_delay=ensemble_sync_delay,
            colocate=colocate_all_tasks,
        )

        if colocate_all_tasks:
            prereq_tasks = []
        else:
            prereq_tasks = [fuzzer_task.task_id]

        coverage_containers = [
            (ContainerType.setup, containers[ContainerType.setup]),
            (ContainerType.coverage, containers[ContainerType.coverage]),
            (ContainerType.readonly_inputs, containers[ContainerType.inputs]),
        ]
        self.logger.info("creating libfuzzer_coverage task")
        self.onefuzz.tasks.create(
            job.job_id,
            TaskType.libfuzzer_coverage,
            target_exe,
            coverage_containers,
            pool_name=pool_name,
            duration=duration,
            vm_count=1,
            reboot_after_setup=reboot_after_setup,
            target_options=target_options,
            target_env=target_env,
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
            target_timeout=crash_report_timeout,
            check_retry_count=check_retry_count,
            debug=debug,
            colocate=colocate_all_tasks or colocate_secondary_tasks,
        )

    def basic(
        self,
        project: str,
        name: str,
        build: str,
        pool_name: str,
        *,
        target_exe: File = File("fuzz.exe"),
        setup_dir: Optional[Directory] = None,
        vm_count: int = 2,
        inputs: Optional[Directory] = None,
        reboot_after_setup: bool = False,
        duration: int = 24,
        target_workers: Optional[int] = None,
        target_options: Optional[List[str]] = None,
        target_env: Optional[Dict[str, str]] = None,
        check_retry_count: Optional[int] = None,
        crash_report_timeout: Optional[int] = None,
        tags: Optional[Dict[str, str]] = None,
        wait_for_running: bool = False,
        wait_for_files: Optional[List[ContainerType]] = None,
        extra_files: Optional[List[File]] = None,
        existing_inputs: Optional[Container] = None,
        dryrun: bool = False,
        notification_config: Optional[NotificationConfig] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        ensemble_sync_delay: Optional[int] = None,
        colocate_all_tasks: bool = False,
        colocate_secondary_tasks: bool = True,
    ) -> Optional[Job]:
        """
        Basic libfuzzer job

        :param bool ensemble_sync_delay: Specify duration between
            syncing inputs during ensemble fuzzing (0 to disable).
        """

        # verify containers exist
        if existing_inputs:
            self.onefuzz.containers.get(existing_inputs)

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
        )

        if existing_inputs:
            self.onefuzz.containers.get(existing_inputs)
            helper.containers[ContainerType.inputs] = existing_inputs
        else:
            helper.define_containers(ContainerType.inputs)

        helper.create_containers()
        helper.setup_notifications(notification_config)

        helper.upload_setup(setup_dir, target_exe, extra_files)
        if inputs:
            helper.upload_inputs(inputs)
        helper.wait_on(wait_for_files, wait_for_running)

        target_exe_blob_name = helper.target_exe_blob_name(target_exe, setup_dir)

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
            target_env=target_env,
            tags=helper.tags,
            crash_report_timeout=crash_report_timeout,
            check_retry_count=check_retry_count,
            debug=debug,
            ensemble_sync_delay=ensemble_sync_delay,
            colocate_all_tasks=colocate_all_tasks,
            colocate_secondary_tasks=colocate_secondary_tasks,
        )

        self.logger.info("done creating tasks")
        helper.wait()
        return helper.job

    def merge(
        self,
        project: str,
        name: str,
        build: str,
        pool_name: str,
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

        target_exe_blob_name = helper.target_exe_blob_name(target_exe, setup_dir)

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
        )

        self.logger.info("done creating tasks")
        helper.wait()
        return helper.job
