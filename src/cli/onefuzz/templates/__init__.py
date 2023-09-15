#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import os
import tempfile
import zipfile
from datetime import timedelta
from typing import Any, Dict, List, Optional, Tuple
from uuid import uuid4

from onefuzztypes.enums import OS, ContainerType, JobState, TaskState
from onefuzztypes.models import Job, NotificationConfig
from onefuzztypes.primitives import Container, Directory, File

from ..job_templates.job_monitor import JobMonitor

ELF_MAGIC = b"\x7fELF"


class StoppedEarly(Exception):
    pass


class ContainerTemplate:
    def __init__(
        self,
        name: Container,
        exists: bool,
        *,
        retention_period: Optional[timedelta] = None,
    ):
        self.name = name
        self.retention_period = retention_period
        # TODO: exists is not yet used/checked
        self.exists = exists

    @staticmethod
    def existing(name: Container) -> "ContainerTemplate":
        return ContainerTemplate(name, True)

    @staticmethod
    def fresh(
        name: Container, *, retention_period: Optional[timedelta] = None
    ) -> "ContainerTemplate":
        return ContainerTemplate(name, False, retention_period=retention_period)


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
        self.to_monitor: Dict[Container, int] = {}

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
        self.wait_for_stopped: bool = False
        self.containers: Dict[ContainerType, ContainerTemplate] = {}
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

    def add_existing_container(
        self, container_type: ContainerType, container: Container
    ) -> None:
        self.containers[container_type] = ContainerTemplate.existing(container)

    def container_name(self, container_type: ContainerType) -> Container:
        return self.containers[container_type].name

    def container_names(self) -> Dict[ContainerType, Container]:
        return {
            container_type: container.name
            for (container_type, container) in self.containers.items()
        }

    def define_containers(self, *types: ContainerType) -> None:
        """
        Define default container set based on provided types

        NOTE: in complex scenarios, containers could be defined elsewhere
        """

        for container_type in types:
            container_name = self.onefuzz.utils.build_container_name(
                container_type=container_type,
                project=self.project,
                name=self.name,
                build=self.build,
                platform=self.platform,
            )
            self.containers[container_type] = ContainerTemplate.fresh(
                container_name,
                retention_period=JobHelper._default_retention_period(container_type),
            )

    @staticmethod
    def _default_retention_period(container_type: ContainerType) -> Optional[timedelta]:
        if container_type == ContainerType.crashdumps:
            return timedelta(days=90)
        return None

    def get_unique_container_name(self, container_type: ContainerType) -> Container:
        return Container(
            "oft-%s-%s"
            % (
                container_type.name.replace("_", "-"),
                uuid4().hex,
            )
        )

    def create_containers(self) -> None:
        for container_type, container in self.containers.items():
            self.logger.info("using container: %s", container.name)
            metadata = {"container_type": container_type.name}
            if container.retention_period is not None:
                # format as ISO8601 period
                # NB: this must match the value used on the server side
                metadata[
                    "onefuzz_retentionperiod"
                ] = f"P{container.retention_period.days}D"

            self.onefuzz.containers.create(container.name, metadata=metadata)

    def delete_container(self, container_name: Container) -> None:
        self.onefuzz.containers.delete(container_name)

    def setup_notifications(self, config: Optional[NotificationConfig]) -> None:
        if not config:
            return

        containers: List[Container] = []
        if ContainerType.unique_reports in self.containers:
            containers.append(self.container_name(ContainerType.unique_reports))
        else:
            containers.append(self.container_name(ContainerType.reports))

        if ContainerType.regression_reports in self.containers:
            containers.append(self.container_name(ContainerType.regression_reports))

        for container in containers:
            self.logger.info("creating notification config for %s", container)
            self.onefuzz.notifications.create(container, config, replace_existing=True)

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
                self.container_name(ContainerType.setup), setup_dir
            )
        else:
            self.logger.info("uploading target exe `%s`" % target_exe)
            self.onefuzz.containers.files.upload_file(
                self.container_name(ContainerType.setup), target_exe
            )

            pdb_path = os.path.splitext(target_exe)[0] + ".pdb"
            if os.path.exists(pdb_path):
                pdb_name = os.path.basename(pdb_path)
                self.onefuzz.containers.files.upload_file(
                    self.container_name(ContainerType.setup), pdb_path, pdb_name
                )
        if setup_files:
            for filename in setup_files:
                self.logger.info("uploading %s", filename)
                self.onefuzz.containers.files.upload_file(
                    self.container_name(ContainerType.setup), filename
                )

    def upload_inputs(self, path: Directory, read_only: bool = False) -> None:
        self.logger.info("uploading inputs: `%s`" % path)
        container_type = ContainerType.inputs
        if read_only:
            container_type = ContainerType.readonly_inputs
        self.onefuzz.containers.files.upload_dir(
            self.container_name(container_type), path
        )

    def upload_inputs_zip(self, path: File) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            with zipfile.ZipFile(path, "r") as zip_ref:
                zip_ref.extractall(tmp_dir)

            self.logger.info("uploading inputs from zip: `%s`" % path)
            self.onefuzz.containers.files.upload_dir(
                self.container_name(ContainerType.inputs), Directory(tmp_dir)
            )

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
            self.container_name(x): len(
                self.onefuzz.containers.files.list(self.container_name(x)).files
            )
            for x in wait_for_files
        }
        self.wait_for_running = wait_for_running

    def check_current_job(self) -> Job:
        job = self.onefuzz.jobs.get(self.job.job_id)
        if job.state in JobState.shutting_down():
            raise StoppedEarly("job unexpectedly stopped early")

        errors = []
        for task in self.onefuzz.tasks.list(
            job_id=self.job.job_id, state=TaskState.shutting_down()
        ):
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
        JobMonitor(self.onefuzz, self.job).wait(
            wait_for_running=self.wait_for_running,
            wait_for_files=self.to_monitor,
            wait_for_stopped=self.wait_for_stopped,
        )

    def setup_relative_blob_name(
        self, local_file: File, setup_dir: Optional[Directory]
    ) -> str:
        # The local file must end up as a remote blob in the setup container. The blob
        # name, which is a virtual `setup`-relative path, must be passed to the tasks as a
        # value relative to the local copy of the setup directory.
        if setup_dir:
            # If we have a `setup_dir`, then `local_file` must occur inside of it. When we
            # upload the `setup_dir`, the blob name will match the `setup_dir`-relative
            # path to the file.
            resolved = os.path.relpath(local_file, setup_dir)

            if resolved.startswith("..") or resolved == local_file:
                raise ValueError(
                    "local file (%s) is not within the setup directory (%s)"
                    % (local_file, setup_dir)
                )

            return resolved
        else:
            # If no `setup_dir` was given, we will upload the file at `local_file` to the
            # root of the setup container created for the user. In that case, the future,
            # setup-relative path to `local_file` is just the filename of `local_file`.
            return os.path.basename(local_file)

    def add_tags(self, tags: Optional[Dict[str, str]]) -> None:
        if tags:
            self.tags.update(tags)


from onefuzz.api import Onefuzz  # noqa: E402
