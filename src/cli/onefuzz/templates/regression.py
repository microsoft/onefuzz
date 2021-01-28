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


class Regression(Command):
    """ Regression job """

    def generic(
        self,
        project: str,
        name: str,
        build: str,
        pool_name: str,
        *,
        crashes: Container = None,
        input_reports: Container = None,
        inputs: Optional[Directory] = None,
        target_exe: File = File("fuzz.exe"),
        tags: Optional[Dict[str, str]] = None,
        notification_config: Optional[NotificationConfig] = None,
        target_env: Optional[Dict[str, str]] = None,
        setup_dir: Optional[Directory] = None,
        reboot_after_setup: bool = False,
        target_options: Optional[List[str]] = None,
        dryrun: bool = False,
        duration: int = 24,
        report_list: Optional[List[str]] = None,
        crash_report_timeout: Optional[int] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        check_retry_count: Optional[int] = None,
        check_fuzzer_help: bool = True,
        fail_on_repro: bool = False,
    ) -> Optional[Job]:

        return self._create_job(
            TaskType.generic_regression,
            project,
            name,
            build,
            pool_name,
            crashes,
            input_reports,
            inputs,
            target_exe,
            tags,
            notification_config,
            target_env,
            setup_dir,
            reboot_after_setup,
            target_options,
            dryrun,
            duration,
            report_list,
            crash_report_timeout,
            debug,
            check_retry_count,
            check_fuzzer_help,
            fail_on_repro,
        )

    def libfuzzer(
        self,
        project: str,
        name: str,
        build: str,
        pool_name: str,
        *,
        crashes: Container = None,
        input_reports: Container = None,
        inputs: Optional[Directory] = None,
        target_exe: File = File("fuzz.exe"),
        tags: Optional[Dict[str, str]] = None,
        notification_config: Optional[NotificationConfig] = None,
        target_env: Optional[Dict[str, str]] = None,
        setup_dir: Optional[Directory] = None,
        reboot_after_setup: bool = False,
        target_options: Optional[List[str]] = None,
        dryrun: bool = False,
        duration: int = 24,
        report_list: Optional[List[str]] = None,
        crash_report_timeout: Optional[int] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        check_retry_count: Optional[int] = None,
        check_fuzzer_help: bool = True,
        fail_on_repro: bool = False,
    ) -> Optional[Job]:

        return self._create_job(
            TaskType.libfuzzer_regression,
            project,
            name,
            build,
            pool_name,
            crashes,
            input_reports,
            inputs,
            target_exe,
            tags,
            notification_config,
            target_env,
            setup_dir,
            reboot_after_setup,
            target_options,
            dryrun,
            duration,
            report_list,
            crash_report_timeout,
            debug,
            check_retry_count,
            check_fuzzer_help,
            fail_on_repro,
        )

    def _create_job(
        self,
        task_type: TaskType,
        project: str,
        name: str,
        build: str,
        pool_name: str,
        crashes: Container = None,
        input_reports: Container = None,
        inputs: Optional[Directory] = None,
        target_exe: File = File("fuzz.exe"),
        tags: Optional[Dict[str, str]] = None,
        notification_config: Optional[NotificationConfig] = None,
        target_env: Optional[Dict[str, str]] = None,
        setup_dir: Optional[Directory] = None,
        reboot_after_setup: bool = False,
        target_options: Optional[List[str]] = None,
        dryrun: bool = False,
        duration: int = 24,
        report_list: Optional[List[str]] = None,
        crash_report_timeout: Optional[int] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        check_retry_count: Optional[int] = None,
        check_fuzzer_help: bool = True,
        fail_on_repro: bool = False,
    ) -> Optional[Job]:

        if not ((crashes and input_reports) or inputs):
            self.logger.error(
                "please specify either the 'crash' and 'input_reports' parameters or the inputs parameter"
            )

        if dryrun:
            return None

        self.logger.info("creating regression task from template")

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
        if crashes:
            if self.onefuzz.containers.get(crashes):
                helper.define_containers(ContainerType.unique_inputs)
            else:
                self.logger.error(f"invalid crash container {crashes}")

        if input_reports:
            if self.onefuzz.containers.get(input_reports):
                helper.define_containers(ContainerType.input_reports)
            else:
                self.logger.error(f"invalid crash container {input_reports}")

        if inputs:
            helper.define_containers(ContainerType.readonly_inputs)

        helper.define_containers(
            ContainerType.setup,
            ContainerType.reports,
            ContainerType.no_repro,
        )

        helper.create_containers()
        helper.setup_notifications(notification_config)
        if inputs:
            helper.upload_inputs(inputs, read_only=True)

        containers = [
            (ContainerType.setup, helper.containers[ContainerType.setup]),
            (ContainerType.reports, helper.containers[ContainerType.reports]),
            (ContainerType.no_repro, helper.containers[ContainerType.no_repro]),
        ]

        if crashes:
            containers.append((ContainerType.crashes, crashes))
        if input_reports:
            containers.append((ContainerType.input_reports, input_reports))

        helper.upload_setup(setup_dir, target_exe)
        target_exe_blob_name = helper.target_exe_blob_name(target_exe, setup_dir)

        self.logger.info("creating regression task")
        regression_task = self.onefuzz.tasks.create(
            helper.job.job_id,
            task_type,
            target_exe_blob_name,
            containers,
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
            check_fuzzer_help=check_fuzzer_help,
            report_list=report_list,
        )
        helper.wait_for_stopping = fail_on_repro

        self.logger.info("done creating tasks")
        helper.wait()
        if fail_on_repro:
            repro_count = len(
                self.onefuzz.containers.files.list(
                    helper.containers[ContainerType.reports],
                    prefix=str(regression_task.task_id),
                ).files
            )
            if repro_count > 0:
                raise Exception("Failure detected")
            else:
                self.logger.info("No Failure detected")

        return helper.job
