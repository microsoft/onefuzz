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

    def regression(
        self,
        project: str,
        name: str,
        build: str,
        pool_name: str,
        crashes: Container,
        input_reports: Container,
        *,
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
    ) -> Optional[Job]:

        if dryrun:
            return None

        self.logger.info("creating libfuzzer merge from template")

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
        if self.onefuzz.containers.get(crashes):
            helper.define_containers(ContainerType.unique_inputs)
        else:
            self.logger.error(f"invalid crash container {crashes}")

        helper.define_containers(
            ContainerType.setup,
            ContainerType.reports,
            ContainerType.unique_reports,
            ContainerType.no_repro,
        )

        helper.create_containers()
        helper.setup_notifications(notification_config)
        containers = [
            (ContainerType.setup, helper.containers[ContainerType.setup]),
            (ContainerType.input_reports, input_reports),
            (ContainerType.crashes, crashes),
            (ContainerType.reports, containers[ContainerType.reports]),
            (ContainerType.no_repro, helper.containers[ContainerType.no_repro]),
        ]

        helper.upload_setup(setup_dir, target_exe)
        target_exe_blob_name = helper.target_exe_blob_name(target_exe, setup_dir)

        self.logger.info("creating libfuzzer_merge task")
        self.onefuzz.tasks.create(
            helper.job.job_id,
            TaskType.generic_regression,
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

        self.logger.info("done creating tasks")
        helper.wait()
        return helper.job
