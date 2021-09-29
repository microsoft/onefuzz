#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Dict, List, Optional

from onefuzztypes.enums import OS, ContainerType, StatsFormat, TaskDebugFlag, TaskType
from onefuzztypes.models import Job, NotificationConfig
from onefuzztypes.primitives import Container, Directory, File, PoolName

from onefuzz.api import Command

from . import JobHelper


class AFL(Command):
    """Pre-defined AFL job"""

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
        target_options: Optional[List[str]] = None,
        supervisor_exe: str = "{tools_dir}/afl-fuzz",
        supervisor_options: List[str] = [
            "-d",
            "-i",
            "{input_corpus}",
            "-o",
            "{runtime_dir}",
            "--",
            "{target_exe}",
            "{target_options}",
        ],
        supervisor_env: Optional[Dict[str, str]] = None,
        supervisor_input_marker: str = "@@",
        tags: Optional[Dict[str, str]] = None,
        wait_for_running: bool = False,
        wait_for_files: Optional[List[ContainerType]] = None,
        afl_container: Optional[Container] = None,
        existing_inputs: Optional[Container] = None,
        dryrun: bool = False,
        notification_config: Optional[NotificationConfig] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        ensemble_sync_delay: Optional[int] = None,
    ) -> Optional[Job]:
        """
        Basic AFL job

        :param Container afl_container: Specify the AFL container to use in the job
        :param bool ensemble_sync_delay: Specify duration between
            syncing inputs during ensemble fuzzing (0 to disable).
        """

        if existing_inputs:
            self.onefuzz.containers.get(existing_inputs)

        if dryrun:
            return None

        # disable ensemble sync if only one VM is used
        if ensemble_sync_delay is None and vm_count == 1:
            ensemble_sync_delay = 0

        self.logger.info("creating afl from template")

        target_options = target_options or ["{input}"]

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
        )
        if existing_inputs:
            self.onefuzz.containers.get(existing_inputs)
            helper.containers[ContainerType.inputs] = existing_inputs
        else:
            helper.define_containers(ContainerType.inputs)

        helper.create_containers()
        helper.setup_notifications(notification_config)
        helper.upload_setup(setup_dir, target_exe)
        if inputs:
            helper.upload_inputs(inputs)
        helper.wait_on(wait_for_files, wait_for_running)

        if (
            len(
                self.onefuzz.containers.files.list(
                    helper.containers[ContainerType.inputs]
                ).files
            )
            == 0
        ):
            raise Exception("AFL requires at least one input")

        target_exe_blob_name = helper.setup_relative_blob_name(target_exe, setup_dir)

        if afl_container is None:
            afl_container = Container(
                "afl-linux" if helper.platform == OS.linux else "afl-windows"
            )

        # verify the AFL container exists
        self.onefuzz.containers.get(afl_container)

        containers = [
            (ContainerType.tools, afl_container),
            (ContainerType.setup, helper.containers[ContainerType.setup]),
            (ContainerType.crashes, helper.containers[ContainerType.crashes]),
            (ContainerType.inputs, helper.containers[ContainerType.inputs]),
        ]

        self.logger.info("creating afl fuzz task")
        fuzzer_task = self.onefuzz.tasks.create(
            helper.job.job_id,
            TaskType.generic_supervisor,
            target_exe_blob_name,
            containers,
            pool_name=pool_name,
            duration=duration,
            vm_count=vm_count,
            reboot_after_setup=reboot_after_setup,
            target_options=target_options,
            supervisor_exe=supervisor_exe,
            supervisor_options=supervisor_options,
            supervisor_env=supervisor_env,
            supervisor_input_marker=supervisor_input_marker,
            stats_file="{runtime_dir}/fuzzer_stats",
            stats_format=StatsFormat.AFL,
            task_wait_for_files=ContainerType.inputs,
            tags=helper.tags,
            debug=debug,
            ensemble_sync_delay=ensemble_sync_delay,
        )

        report_containers = [
            (ContainerType.setup, helper.containers[ContainerType.setup]),
            (ContainerType.crashes, helper.containers[ContainerType.crashes]),
            (ContainerType.reports, helper.containers[ContainerType.reports]),
            (
                ContainerType.unique_reports,
                helper.containers[ContainerType.unique_reports],
            ),
        ]

        self.logger.info("creating generic_crash_report task")
        self.onefuzz.tasks.create(
            helper.job.job_id,
            TaskType.generic_crash_report,
            target_exe_blob_name,
            report_containers,
            pool_name=pool_name,
            duration=duration,
            vm_count=1,
            reboot_after_setup=reboot_after_setup,
            target_options=target_options,
            check_debugger=True,
            tags=tags,
            prereq_tasks=[fuzzer_task.task_id],
            debug=debug,
        )

        self.logger.info("done creating tasks")
        helper.wait()
        return helper.job
