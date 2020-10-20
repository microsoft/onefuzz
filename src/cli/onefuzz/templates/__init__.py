#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import os
import tempfile
import zipfile
from typing import Any, Dict, List, Optional, Tuple

from onefuzztypes.enums import OS, ContainerType, TaskState
from onefuzztypes.models import Job, NotificationConfig
from onefuzztypes.primitives import Container, Directory, File

from onefuzz.backend import wait

ELF_MAGIC = b"\x7fELF"
DEFAULT_LINUX_IMAGE = "Canonical:UbuntuServer:18.04-LTS:latest"
DEFAULT_WINDOWS_IMAGE = "MicrosoftWindowsDesktop:Windows-10:rs5-pro:latest"


class StoppedEarly(Exception):
    pass


class JobHelper:
    def __init__(
        self,
        onefuzz: "Onefuzz",
        logger: Any,
        project: str,
        name: str,
        build: str,
        duration: int,
        *,
        pool_name: Optional[str] = None,
        target_exe: File,
        platform: Optional[OS] = None,
        job: Optional[Job] = None,
    ):
        self.onefuzz = onefuzz
        self.logger = logger
        self.project = project
        self.name = name
        self.build = build
        self.to_monitor: Dict[str, int] = {}

        if platform is None:
            self.platform = JobHelper.get_platform(target_exe)
        else:
            self.platform = platform

        if pool_name:
            pool = self.onefuzz.pools.get(pool_name)
            if pool.os != self.platform:
                raise Exception(
                    "mismatched os. pool: %s target: %s"
                    % (pool.os.name, self.platform.name)
                )

        self.wait_for_running: bool = False
        self.containers: Dict[ContainerType, Container] = {}
        self.tags: Dict[str, str] = {"project": project, "name": name, "build": build}
        if job is None:
            self.onefuzz.versions.check()
            self.logger.info("creating job (runtime: %d hours)", duration)
            self.job = self.onefuzz.jobs.create(
                self.project, self.name, self.build, duration=duration
            )
            self.logger.info("created job: %s" % self.job.job_id)
        else:
            self.job = job

    def define_containers(self, *types: ContainerType) -> None:
        """
        Define default container set based on provided types

        NOTE: in complex scenarios, containers could be defined elsewhere
        """

        for container_type in types:
            if container_type == ContainerType.setup:
                guid = self.onefuzz.utils.namespaced_guid(
                    self.project,
                    self.name,
                    build=self.build,
                    platform=self.platform.name,
                )
            else:
                guid = self.onefuzz.utils.namespaced_guid(self.project, self.name)

            self.containers[container_type] = Container(
                "oft-%s-%s"
                % (
                    container_type.name.replace("_", "-"),
                    guid.hex,
                )
            )

    def create_containers(self) -> None:
        all_containers = [x.name for x in self.onefuzz.containers.list()]
        for (container_type, container_name) in self.containers.items():
            if container_name in all_containers:
                self.logger.info("using container: %s", container_name)
            else:
                self.logger.info("creating container: %s", container_name)
                self.onefuzz.containers.create(
                    container_name, metadata={"container_type": container_type.name}
                )

    def setup_notifications(self, config: Optional[NotificationConfig]) -> None:
        if not config:
            return

        container: Optional[str] = None
        if ContainerType.unique_reports in self.containers:
            container = self.containers[ContainerType.unique_reports]
        else:
            container = self.containers[ContainerType.reports]

        if not container:
            return

        config_dict = json.loads(config.json())
        for entry in self.onefuzz.notifications.list():
            if entry.container == container and entry.config == config_dict:
                self.logger.debug(
                    "notification already exists: %s", entry.notification_id
                )
                return

        self.logger.info("creating notification config for %s", container)
        self.onefuzz.notifications.create(container, config)

    def upload_setup(
        self,
        setup_dir: Optional[Directory],
        target_exe: File,
        setup_files: Optional[List[File]] = None,
    ) -> None:
        if setup_dir:
            target_exe_in_setup_dir = os.path.abspath(target_exe).startswith(
                os.path.abspath(setup_dir)
            )
            if not target_exe_in_setup_dir:
                raise Exception(
                    "target exe `%s` does not occur in setup dir `%s`"
                    % (target_exe, setup_dir)
                )

            self.logger.info("uploading setup dir `%s`" % setup_dir)
            self.onefuzz.containers.files.upload_dir(
                self.containers[ContainerType.setup], setup_dir
            )
        else:
            self.logger.info("uploading target exe `%s`" % target_exe)
            self.onefuzz.containers.files.upload_file(
                self.containers[ContainerType.setup], target_exe
            )

            pdb_path = os.path.splitext(target_exe)[0] + ".pdb"
            if os.path.exists(pdb_path):
                pdb_name = os.path.basename(pdb_path)
                self.onefuzz.containers.files.upload_file(
                    self.containers[ContainerType.setup], pdb_path, pdb_name
                )
        if setup_files:
            for filename in setup_files:
                self.logger.info("uploading %s", filename)
                self.onefuzz.containers.files.upload_file(
                    self.containers[ContainerType.setup], filename
                )

    def upload_inputs(self, path: Directory, read_only: bool = False) -> None:
        self.logger.info("uploading inputs: `%s`" % path)
        container_type = ContainerType.inputs
        if read_only:
            container_type = ContainerType.readonly_inputs
        self.onefuzz.containers.files.upload_dir(self.containers[container_type], path)

    def upload_inputs_zip(self, path: File) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            with zipfile.ZipFile(path, "r") as zip_ref:
                zip_ref.extractall(tmp_dir)

            self.logger.info("uploading inputs from zip: `%s`" % path)
            self.onefuzz.containers.files.upload_dir(
                self.containers[ContainerType.inputs], tmp_dir
            )

    @classmethod
    def get_image(_cls, platform: OS) -> str:
        if platform == OS.linux:
            return DEFAULT_LINUX_IMAGE
        else:
            return DEFAULT_WINDOWS_IMAGE

    @classmethod
    def get_platform(_cls, target_exe: File) -> OS:
        with open(target_exe, "rb") as handle:
            header = handle.read(4)
        if header == ELF_MAGIC:
            return OS.linux
        else:
            return OS.windows

    def wait_on(
        self, wait_for_files: Optional[List[ContainerType]], wait_for_running: bool
    ) -> None:
        if wait_for_files is None:
            wait_for_files = []

        self.to_monitor = {
            self.containers[x]: len(
                self.onefuzz.containers.files.list(self.containers[x]).files
            )
            for x in wait_for_files
        }
        self.wait_for_running = wait_for_running

    def check_current_job(self) -> Job:
        job = self.onefuzz.jobs.get(self.job.job_id)
        if job.state in ["stopped", "stopping"]:
            raise StoppedEarly("job unexpectedly stopped early")

        errors = []
        for task in self.onefuzz.tasks.list(job_id=self.job.job_id):
            if task.state in ["stopped", "stopping"]:
                if task.error:
                    errors.append("%s: %s" % (task.config.task.type, task.error))
                else:
                    errors.append("%s" % task.config.task.type)

        if errors:
            raise StoppedEarly("tasks stopped unexpectedly.\n%s" % "\n".join(errors))
        return job

    def get_waiting(self) -> List[str]:
        tasks = self.onefuzz.tasks.list(job_id=self.job.job_id)
        waiting = [
            "%s:%s" % (x.config.task.type.name, x.state.name)
            for x in tasks
            if x.state not in TaskState.has_started()
        ]
        return waiting

    def is_running(self) -> Tuple[bool, str, Any]:
        waiting = self.get_waiting()
        return (not waiting, "waiting on: %s" % ", ".join(sorted(waiting)), None)

    def has_files(self) -> Tuple[bool, str, Any]:
        self.check_current_job()

        new = {
            x: len(self.onefuzz.containers.files.list(x).files)
            for x in self.to_monitor.keys()
        }

        for container in new:
            if new[container] > self.to_monitor[container]:
                del self.to_monitor[container]
        return (
            not self.to_monitor,
            "waiting for new files: %s" % ", ".join(self.to_monitor.keys()),
            None,
        )

    def wait(self) -> None:
        if self.wait_for_running:
            wait(self.is_running)
            self.logger.info("tasks started")

        if self.to_monitor:
            wait(self.has_files)
            self.logger.info("new files found")

    def target_exe_blob_name(
        self, target_exe: File, setup_dir: Optional[Directory]
    ) -> str:
        # The target executable must end up in the setup container, and the
        # `target_exe` value passed to the tasks must be the name of the target
        # exe _as a blob_ in the setup container.
        if setup_dir:
            # If the user set a `setup_dir`, then `target_exe` must occur inside
            # of it. When we upload the `setup_dir`, the blob name agrees with
            # the `setup_dir`-relative path of the target.
            resolved = os.path.relpath(target_exe, setup_dir)
            if resolved.startswith("..") or resolved == target_exe:
                raise ValueError(
                    "target_exe (%s) is not within the setup directory (%s)"
                    % (target_exe, setup_dir)
                )
            return resolved
        else:
            # If no `setup_dir` was given, we will upload `target_exe` to the
            # root of the setup container created for the user. In that case,
            # the `target_exe` name is just the filename of `target_exe`.
            return os.path.basename(target_exe)

    def add_tags(self, tags: Optional[Dict[str, str]]) -> None:
        if tags:
            self.tags.update(tags)


from onefuzz.api import Onefuzz  # noqa: E402
