#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
from typing import Dict, List, Optional

from onefuzztypes.enums import ContainerType, TaskDebugFlag, TaskType
from onefuzztypes.models import NotificationConfig, RegressionReport
from onefuzztypes.primitives import Container, Directory, File, PoolName

from onefuzz.api import Command

from . import JobHelper


class Regression(Command):
    """Regression job"""

    def _check_regression(self, container: Container, file: File) -> bool:
        content = self.onefuzz.containers.files.get(Container(container), file)
        as_str = content.decode()
        as_obj = json.loads(as_str)
        report = RegressionReport.parse_obj(as_obj)

        if report.crash_test_result.crash_report is not None:
            return True

        if report.crash_test_result.no_repro is not None:
            return False

        raise Exception("invalid crash report")

    def generic(
        self,
        project: str,
        name: str,
        build: str,
        pool_name: PoolName,
        *,
        reports: Optional[List[str]] = None,
        crashes: Optional[List[File]] = None,
        target_exe: File = File("fuzz.exe"),
        tags: Optional[Dict[str, str]] = None,
        notification_config: Optional[NotificationConfig] = None,
        target_env: Optional[Dict[str, str]] = None,
        setup_dir: Optional[Directory] = None,
        reboot_after_setup: bool = False,
        target_options: Optional[List[str]] = None,
        dryrun: bool = False,
        duration: int = 24,
        crash_report_timeout: Optional[int] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        check_retry_count: Optional[int] = None,
        check_fuzzer_help: bool = True,
        delete_input_container: bool = True,
        check_regressions: bool = False,
    ) -> None:
        """
        generic regression task

        :param File crashes: Specify crashing input files to check in the regression task
        :param str reports: Specify specific report names to verify in the regression task
        :param bool check_regressions: Specify if exceptions should be thrown on finding crash regressions
        :param bool delete_input_container: Specify wether or not to delete the input container
        """

        self._create_job(
            TaskType.generic_regression,
            project,
            name,
            build,
            pool_name,
            crashes=crashes,
            reports=reports,
            target_exe=target_exe,
            tags=tags,
            notification_config=notification_config,
            target_env=target_env,
            setup_dir=setup_dir,
            reboot_after_setup=reboot_after_setup,
            target_options=target_options,
            dryrun=dryrun,
            duration=duration,
            crash_report_timeout=crash_report_timeout,
            debug=debug,
            check_retry_count=check_retry_count,
            check_fuzzer_help=check_fuzzer_help,
            delete_input_container=delete_input_container,
            check_regressions=check_regressions,
        )

    def libfuzzer(
        self,
        project: str,
        name: str,
        build: str,
        pool_name: PoolName,
        *,
        reports: Optional[List[str]] = None,
        crashes: Optional[List[File]] = None,
        target_exe: File = File("fuzz.exe"),
        tags: Optional[Dict[str, str]] = None,
        notification_config: Optional[NotificationConfig] = None,
        target_env: Optional[Dict[str, str]] = None,
        setup_dir: Optional[Directory] = None,
        reboot_after_setup: bool = False,
        target_options: Optional[List[str]] = None,
        dryrun: bool = False,
        duration: int = 24,
        crash_report_timeout: Optional[int] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        check_retry_count: Optional[int] = None,
        check_fuzzer_help: bool = True,
        delete_input_container: bool = True,
        check_regressions: bool = False,
    ) -> None:

        """
        libfuzzer regression task

        :param File crashes: Specify crashing input files to check in the regression task
        :param str reports: Specify specific report names to verify in the regression task
        :param bool check_regressions: Specify if exceptions should be thrown on finding crash regressions
        :param bool delete_input_container: Specify wether or not to delete the input container
        """

        self._create_job(
            TaskType.libfuzzer_regression,
            project,
            name,
            build,
            pool_name,
            crashes=crashes,
            reports=reports,
            target_exe=target_exe,
            tags=tags,
            notification_config=notification_config,
            target_env=target_env,
            setup_dir=setup_dir,
            reboot_after_setup=reboot_after_setup,
            target_options=target_options,
            dryrun=dryrun,
            duration=duration,
            crash_report_timeout=crash_report_timeout,
            debug=debug,
            check_retry_count=check_retry_count,
            check_fuzzer_help=check_fuzzer_help,
            delete_input_container=delete_input_container,
            check_regressions=check_regressions,
        )

    def _create_job(
        self,
        task_type: TaskType,
        project: str,
        name: str,
        build: str,
        pool_name: PoolName,
        *,
        crashes: Optional[List[File]] = None,
        reports: Optional[List[str]] = None,
        target_exe: File = File("fuzz.exe"),
        tags: Optional[Dict[str, str]] = None,
        notification_config: Optional[NotificationConfig] = None,
        target_env: Optional[Dict[str, str]] = None,
        setup_dir: Optional[Directory] = None,
        reboot_after_setup: bool = False,
        target_options: Optional[List[str]] = None,
        dryrun: bool = False,
        duration: int = 24,
        crash_report_timeout: Optional[int] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        check_retry_count: Optional[int] = None,
        check_fuzzer_help: bool = True,
        delete_input_container: bool = True,
        check_regressions: bool = False,
    ) -> None:

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

        helper.define_containers(
            ContainerType.setup,
            ContainerType.crashes,
            ContainerType.reports,
            ContainerType.no_repro,
            ContainerType.unique_reports,
            ContainerType.regression_reports,
        )

        containers = [
            (ContainerType.setup, helper.containers[ContainerType.setup]),
            (ContainerType.crashes, helper.containers[ContainerType.crashes]),
            (ContainerType.reports, helper.containers[ContainerType.reports]),
            (ContainerType.no_repro, helper.containers[ContainerType.no_repro]),
            (
                ContainerType.unique_reports,
                helper.containers[ContainerType.unique_reports],
            ),
            (
                ContainerType.regression_reports,
                helper.containers[ContainerType.regression_reports],
            ),
        ]

        if crashes:
            helper.containers[
                ContainerType.readonly_inputs
            ] = helper.get_unique_container_name(ContainerType.readonly_inputs)
            containers.append(
                (
                    ContainerType.readonly_inputs,
                    helper.containers[ContainerType.readonly_inputs],
                )
            )

        helper.create_containers()
        if crashes:
            for file in crashes:
                self.onefuzz.containers.files.upload_file(
                    helper.containers[ContainerType.readonly_inputs], file
                )

        helper.setup_notifications(notification_config)

        helper.upload_setup(setup_dir, target_exe)
        target_exe_blob_name = helper.target_exe_blob_name(target_exe, setup_dir)

        self.logger.info("creating regression task")
        task = self.onefuzz.tasks.create(
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
            report_list=reports,
        )
        helper.wait_for_stopped = check_regressions

        self.logger.info("done creating tasks")
        helper.wait()

        if check_regressions:
            task = self.onefuzz.tasks.get(task.task_id)
            if task.error:
                raise Exception("task failed: %s", task.error)

            container = helper.containers[ContainerType.regression_reports]
            for filename in self.onefuzz.containers.files.list(container).files:
                self.logger.info("checking file: %s", filename)
                if self._check_regression(container, File(filename)):
                    raise Exception(f"regression identified: {filename}")
            self.logger.info("no regressions")

        if (
            delete_input_container
            and ContainerType.readonly_inputs in helper.containers
        ):
            helper.delete_container(helper.containers[ContainerType.readonly_inputs])
