#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Dict, List, Optional

from onefuzztypes.enums import OS, ContainerType, TaskDebugFlag, TaskType
from onefuzztypes.models import Job, NotificationConfig
from onefuzztypes.primitives import Container, Directory, File, PoolName

from onefuzz.api import Command

from . import JobHelper


class Radamsa(Command):
    """Pre-defined Radamsa job"""

    def basic(
        self,
        project: str,
        name: str,
        build: str,
        *,
        pool_name: PoolName,
        target_exe: File = File("fuzz.exe"),
        setup_dir: Optional[Directory] = None,
        vm_count: int = 2,
        inputs: Optional[Directory] = None,
        reboot_after_setup: bool = False,
        duration: int = 24,
        generator_exe: Optional[str] = None,
        target_options: List[str] = ["{input}"],
        target_env: Optional[Dict[str, str]] = None,
        tags: Optional[Dict[str, str]] = None,
        wait_for_running: bool = False,
        wait_for_files: Optional[List[ContainerType]] = None,
        generator_options: Optional[List[str]] = None,
        radamsa_seed: Optional[int] = None,
        analyzer_exe: Optional[str] = None,
        analyzer_options: Optional[List[str]] = None,
        analyzer_env: Optional[Dict[str, str]] = None,
        existing_inputs: Optional[Container] = None,
        check_asan_log: bool = False,
        check_retry_count: Optional[int] = None,
        disable_check_debugger: bool = False,
        dryrun: bool = False,
        notification_config: Optional[NotificationConfig] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        ensemble_sync_delay: Optional[int] = None,
        target_timeout: Optional[int] = None,
    ) -> Optional[Job]:
        """
        Basic radamsa job

        :param bool ensemble_sync_delay: Specify duration between
            syncing inputs during ensemble fuzzing (0 to disable).
        """

        if inputs is None and existing_inputs is None:
            raise Exception("radamsa requires inputs")

        if dryrun:
            return None

        # disable ensemble sync if only one VM is used
        if ensemble_sync_delay is None and vm_count == 1:
            ensemble_sync_delay = 0

        self.logger.info("creating radamsa from template")

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
            ContainerType.crashes,
            ContainerType.reports,
            ContainerType.unique_reports,
            ContainerType.no_repro,
            ContainerType.analysis,
        )
        if existing_inputs:
            self.onefuzz.containers.get(existing_inputs)
            helper.containers[ContainerType.readonly_inputs] = existing_inputs
        else:
            helper.define_containers(ContainerType.readonly_inputs)
        helper.create_containers()
        helper.setup_notifications(notification_config)

        helper.upload_setup(setup_dir, target_exe)
        if inputs:
            helper.upload_inputs(inputs, read_only=True)
        helper.wait_on(wait_for_files, wait_for_running)

        if (
            len(
                self.onefuzz.containers.files.list(
                    helper.containers[ContainerType.readonly_inputs]
                ).files
            )
            == 0
        ):
            raise Exception("Radamsa requires at least one input file")

        target_exe_blob_name = helper.setup_relative_blob_name(target_exe, setup_dir)

        tools = Container(
            "radamsa-linux" if helper.platform == OS.linux else "radamsa-win64"
        )
        if generator_exe is None:
            generator_exe = (
                "{tools_dir}/radamsa"
                if helper.platform == OS.linux
                else "{tools_dir}\\radamsa.exe"
            )
        rename_output = True
        if generator_options is None:
            generator_options, rename_output = (
                [
                    "-H",
                    "sha256",
                    "-o",
                    "{generated_inputs}/input-%h.%s",
                    "-n",
                    "100",
                    "-r",
                    "{input_corpus}",
                ],
                False,
            )

        if radamsa_seed is not None:
            generator_options += ["--seed", str(radamsa_seed)]

        self.logger.info("creating radamsa task")

        containers = [
            (ContainerType.tools, tools),
            (ContainerType.setup, helper.containers[ContainerType.setup]),
            (ContainerType.crashes, helper.containers[ContainerType.crashes]),
            (
                ContainerType.readonly_inputs,
                helper.containers[ContainerType.readonly_inputs],
            ),
        ]

        fuzzer_task = self.onefuzz.tasks.create(
            helper.job.job_id,
            TaskType.generic_generator,
            target_exe_blob_name,
            containers,
            pool_name=pool_name,
            duration=duration,
            vm_count=vm_count,
            reboot_after_setup=reboot_after_setup,
            target_options=target_options,
            target_env=target_env,
            generator_exe=generator_exe,
            generator_options=generator_options,
            check_asan_log=check_asan_log,
            check_debugger=not disable_check_debugger,
            tags=helper.tags,
            rename_output=rename_output,
            debug=debug,
            ensemble_sync_delay=ensemble_sync_delay,
            target_timeout=target_timeout,
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

        self.logger.info("creating generic_crash_report task")
        self.onefuzz.tasks.create(
            helper.job.job_id,
            TaskType.generic_crash_report,
            target_exe_blob_name,
            report_containers,
            duration=duration,
            vm_count=1,
            pool_name=pool_name,
            reboot_after_setup=reboot_after_setup,
            target_options=target_options,
            target_env=target_env,
            tags=helper.tags,
            check_asan_log=check_asan_log,
            check_debugger=not disable_check_debugger,
            check_retry_count=check_retry_count,
            prereq_tasks=[fuzzer_task.task_id],
            debug=debug,
            target_timeout=target_timeout,
        )

        if helper.platform == OS.windows:
            if analyzer_exe is None:
                analyzer_exe = "cdb.exe"
            if analyzer_options is None:
                analyzer_options = [
                    "-c",
                    "!analyze;q",
                    "-logo",
                    "{output_dir}\\{input_file_name_no_ext}.report",
                    "{target_exe}",
                    "{target_options}",
                ]

            self.logger.info("creating custom analysis")

            analysis_containers = [
                (ContainerType.setup, helper.containers[ContainerType.setup]),
                (ContainerType.tools, tools),
                (ContainerType.analysis, helper.containers[ContainerType.analysis]),
                (ContainerType.crashes, helper.containers[ContainerType.crashes]),
            ]

            self.onefuzz.tasks.create(
                helper.job.job_id,
                TaskType.generic_analysis,
                target_exe_blob_name,
                analysis_containers,
                duration=duration,
                pool_name=pool_name,
                vm_count=vm_count,
                reboot_after_setup=reboot_after_setup,
                target_options=target_options,
                target_env=target_env,
                analyzer_exe=analyzer_exe,
                analyzer_options=analyzer_options,
                analyzer_env=analyzer_env,
                tags=helper.tags,
                prereq_tasks=[fuzzer_task.task_id],
                debug=debug,
                target_timeout=target_timeout,
            )

        self.logger.info("done creating tasks")
        helper.wait()
        return helper.job
