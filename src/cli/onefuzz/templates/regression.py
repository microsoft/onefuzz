#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Dict, List, Optional

from onefuzztypes.enums import ContainerType, TaskDebugFlag, TaskType
from onefuzztypes.models import Job, NotificationConfig
from onefuzztypes.primitives import Directory, File, PoolName

from onefuzz.api import Command

from . import JobHelper
from .template_error import TemplateError

# Special exit code value used by git bisect to determine if a commit should be skipped
# https://git-scm.com/docs/git-bisect#_bisect_run
GIT_BISSECT_SKIP_CODE = 125


class Regression(Command):
    """ Regression job """

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
        fail_on_repro: bool = False,
        delete_input_container: bool = True,
    ) -> Optional[Job]:
        """
        generic regression task

        :param File inputs: Specify a directory of inptus to use in the regression task
        :param str reports: Specify specific report names to verify in the regression task
        :param bool fail_on_repro: Specify wether or not to throw an exception if a repro was generated
        :param bool delete_input_container: Specify wether or not to delete the input container
        """

        return self._create_job(
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
            fail_on_repro=fail_on_repro,
            delete_input_container=delete_input_container,
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
        fail_on_repro: bool = False,
        delete_input_container: bool = True,
    ) -> Optional[Job]:

        """
        generic regression task

        :param File inputs: Specify a directory of inptus to use in the regression task
        :param str reports: Specify specific report names to verify in the regression task
        :param bool fail_on_repro: Specify wether or not to throw an exception if a repro was generated
        :param bool delete_input_container: Specify wether or not to delete the input container
        """

        return self._create_job(
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
            fail_on_repro=fail_on_repro,
            delete_input_container=delete_input_container,
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
        fail_on_repro: bool = False,
        delete_input_container: bool = True,
    ) -> Optional[Job]:

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

        if crashes:
            helper.containers[
                ContainerType.readonly_inputs
            ] = helper.get_unique_container_name(ContainerType.readonly_inputs)

        helper.define_containers(
            ContainerType.setup,
            ContainerType.reports,
            ContainerType.no_repro,
            ContainerType.regression_reports,
        )

        helper.create_containers()
        if crashes:
            for file in crashes:
                self.onefuzz.containers.files.upload_file(
                    helper.containers[ContainerType.unique_inputs], file
                )

        helper.setup_notifications(notification_config)
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
            report_list=reports,
        )
        helper.wait_for_stopping = fail_on_repro

        self.logger.info("done creating tasks")
        helper.wait()

        if fail_on_repro:
            if helper.job.error:
                raise TemplateError(
                    "unable to run the the regression", GIT_BISSECT_SKIP_CODE
                )

            repro_count = len(
                self.onefuzz.containers.files.list(
                    helper.containers[ContainerType.regression_reports],
                    prefix=str(regression_task.task_id),
                ).files
            )
            if repro_count > 0:
                raise TemplateError("Failure detected", -1)
            else:
                self.logger.info("No Failure detected")

        if (
            delete_input_container
            and ContainerType.readonly_inputs in helper.containers
        ):
            helper.delete_container(helper.containers[ContainerType.readonly_inputs])

        return helper.job
