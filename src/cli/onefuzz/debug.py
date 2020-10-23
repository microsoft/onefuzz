#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import os
import shutil
import subprocess  # nosec
import tempfile
from typing import Any, List, Optional, Tuple
from urllib.parse import urlparse
from uuid import UUID
import time

from onefuzztypes.enums import ContainerType, TaskType
from onefuzztypes.models import BlobRef, NodeAssignment, Report, Task
from onefuzztypes.primitives import Directory

from onefuzz.api import UUID_EXPANSION, Command

from .backend import wait
from .rdp import rdp_connect
from .ssh import ssh_connect

EMPTY_SHA256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
ZERO_SHA256 = "0" * len(EMPTY_SHA256)


class DebugRepro(Command):
    """ Debug repro instances """

    def _disambiguate(self, vm_id: UUID_EXPANSION) -> str:
        return str(
            self.onefuzz.repro._disambiguate_uuid(
                "vm_id",
                vm_id,
                lambda: [str(x.vm_id) for x in self.onefuzz.repro.list()],
            )
        )

    def _info(self) -> Tuple[str, str]:
        info = self.onefuzz.info.get()
        return info.resource_group, info.subscription

    def ssh(self, vm_id: str) -> None:
        vm_id = self._disambiguate(vm_id)
        repro = self.onefuzz.repro.get(vm_id)
        if repro.ip is None:
            raise Exception("missing IP: %s" % repro)
        if repro.auth is None:
            raise Exception("missing Auth: %s" % repro)

        with ssh_connect(repro.ip, repro.auth.private_key, call=True):
            pass

    def rdp(self, vm_id: str) -> None:
        vm_id = self._disambiguate(vm_id)
        repro = self.onefuzz.repro.get(vm_id)
        if repro.ip is None:
            raise Exception("missing IP: %s" % repro)
        if repro.auth is None:
            raise Exception("missing Auth: %s" % repro)

        RDP_PORT = 3389
        with rdp_connect(repro.ip, repro.auth.password, port=RDP_PORT):
            return


class DebugScaleset(Command):
    """ Debug tasks """

    def _get_proxy_setup(
        self, scaleset_id: UUID, machine_id: UUID, port: int, duration: Optional[int]
    ) -> Tuple[bool, str, Optional[Tuple[str, int]]]:
        proxy = self.onefuzz.scaleset_proxy.create(
            scaleset_id, machine_id, port, duration=duration
        )
        if proxy.ip is None:
            return (False, "waiting on proxy", None)

        return (True, "waiting on proxy", (proxy.ip, proxy.forward.src_port))

    def rdp(
        self,
        scaleset_id: UUID_EXPANSION,
        machine_id: UUID_EXPANSION,
        duration: Optional[int] = 1,
    ) -> None:
        (
            scaleset,
            machine_id_expanded,
        ) = self.onefuzz.scalesets._expand_scaleset_machine(
            scaleset_id, machine_id, include_auth=True
        )

        RDP_PORT = 3389
        setup = wait(
            lambda: self._get_proxy_setup(
                scaleset.scaleset_id, machine_id_expanded, RDP_PORT, duration
            )
        )
        if setup is None:
            raise Exception("no proxy for RDP port configured")

        if scaleset.auth is None:
            raise Exception("auth is not available for scaleset")

        ip, port = setup
        with rdp_connect(ip, scaleset.auth.password, port=port):
            return

    def ssh(
        self,
        scaleset_id: UUID_EXPANSION,
        machine_id: UUID_EXPANSION,
        duration: Optional[int] = 1,
        command: Optional[str] = None,
    ) -> None:
        (
            scaleset,
            machine_id_expanded,
        ) = self.onefuzz.scalesets._expand_scaleset_machine(
            scaleset_id, machine_id, include_auth=True
        )

        SSH_PORT = 22
        setup = wait(
            lambda: self._get_proxy_setup(
                scaleset.scaleset_id, machine_id_expanded, SSH_PORT, duration
            )
        )
        if setup is None:
            raise Exception("no proxy for SSH port configured")

        ip, port = setup

        if scaleset.auth is None:
            raise Exception("auth is not available for scaleset")

        with ssh_connect(
            ip, scaleset.auth.private_key, port=port, call=True, command=command
        ):
            return


class DebugTask(Command):
    """ Debug a specific job """

    def list_nodes(self, task_id: UUID_EXPANSION) -> Optional[List[NodeAssignment]]:
        task = self.onefuzz.tasks.get(task_id)
        return task.nodes

    def _get_node(
        self, task_id: UUID_EXPANSION, node_id: Optional[UUID]
    ) -> Tuple[UUID, UUID]:
        nodes = self.list_nodes(task_id)
        if not nodes:
            raise Exception("task is not currently executing on nodes")

        if node_id is not None:
            for node in nodes:
                if node.node_id == node_id and node.scaleset_id is not None:
                    return (node.scaleset_id, node.node_id)
            raise Exception("unable to find scaleset with node_id")

        for node in nodes:
            if node.scaleset_id:
                return (node.scaleset_id, node.node_id)

        raise Exception("unable to find scaleset node running on task")

    def disable_node_reset(
        self, task_id: UUID_EXPANSION, *, node_id: Optional[UUID] = None
    ):
        while True:
            try:
                scaleset_id, node_id = self._get_node(task_id=task_id, node_id=node_id)
                self.onefuzz.nodes.update(node_id, manual_reset_override=True)
                break
            except Exception as err:
                if "task is not currently executing on nodes" not in str(err):
                    raise err
                time.sleep(1)

    def ssh(
        self,
        task_id: UUID_EXPANSION,
        *,
        node_id: Optional[UUID] = None,
        duration: Optional[int] = 1,
    ) -> None:
        scaleset_id, node_id = self._get_node(task_id, node_id)
        return self.onefuzz.debug.scalesets.ssh(scaleset_id, node_id, duration=duration)

    def rdp(
        self,
        task_id: UUID_EXPANSION,
        *,
        node_id: Optional[UUID] = None,
        duration: Optional[int] = 1,
    ) -> None:
        scaleset_id, node_id = self._get_node(task_id, node_id)
        return self.onefuzz.debug.scalesets.rdp(scaleset_id, node_id, duration=duration)


class DebugJobTask(Command):
    """ Debug a task for a specific job """

    def _get_task(self, job_id: UUID_EXPANSION, task_type: TaskType) -> UUID:
        for task in self.onefuzz.tasks.list(job_id=job_id):
            if task.config.task.type == task_type:
                return task.task_id

        raise Exception(
            "unable to find task type %s for job:%s" % (task_type.name, job_id)
        )

    def ssh(
        self,
        job_id: UUID_EXPANSION,
        task_type: TaskType,
        *,
        duration: Optional[int] = 1,
    ) -> None:
        """ SSH into the first node running the specified task type in the job """
        return self.onefuzz.debug.task.ssh(
            self._get_task(job_id, task_type), duration=duration
        )

    def rdp(
        self,
        job_id: UUID_EXPANSION,
        task_type: TaskType,
        *,
        duration: Optional[int] = 1,
    ) -> None:
        """ RDP into the first node running the specified task type in the job """
        return self.onefuzz.debug.task.rdp(
            self._get_task(job_id, task_type), duration=duration
        )


class DebugJob(Command):
    """ Debug a specific Job """

    def __init__(self, onefuzz: Any, logger: logging.Logger):
        super().__init__(onefuzz, logger)
        self.task = DebugJobTask(onefuzz, logger)

    def download_files(self, job_id: UUID_EXPANSION, output: Directory) -> None:
        """ Download the containers by container type for each task in the specified job """

        azcopy = os.environ.get("AZCOPY") or shutil.which("azcopy")
        if not azcopy:
            raise Exception(
                "unable to find 'azcopy' in path or AZCOPY environment variable"
            )

        to_download = {}
        tasks = self.onefuzz.tasks.list(job_id=job_id, state=None)
        if not tasks:
            raise Exception("no tasks with job_id:%s" % job_id)

        for task in tasks:
            for container in task.config.containers:
                info = self.onefuzz.containers.get(container.name)
                name = os.path.join(container.type.name, container.name)
                to_download[name] = info.sas_url

        for name in to_download:
            outdir = os.path.join(output, name)
            if not os.path.exists(outdir):
                os.makedirs(outdir)
            self.logger.info("downloading: %s", name)
            subprocess.check_output([azcopy, "sync", to_download[name], outdir])


class DebugNotification(Command):
    """ Debug notification integrations """

    def _get_container(
        self, task: Task, container_type: ContainerType
    ) -> Optional[str]:
        for container in task.config.containers:
            if container.type == container_type:
                return container.name
        return None

    def _get_storage_account(self, container_name: str) -> str:
        sas_url = self.onefuzz.containers.get(container_name).sas_url
        _, netloc, _, _, _, _ = urlparse(sas_url)
        return netloc.split(".")[0]

    def job(
        self,
        job_id: str,
        *,
        report_container_type: ContainerType = ContainerType.unique_reports,
        crash_name: str = "fake-crash-sample",
    ) -> None:
        """ Inject a report into the first crash reporting task in the specified job """

        tasks = self.onefuzz.tasks.list(job_id=job_id, state=[])
        for task in tasks:
            if task.config.task.type in [
                TaskType.libfuzzer_crash_report,
                TaskType.generic_crash_report,
            ]:
                self.task(
                    str(task.task_id),
                    report_container_type=report_container_type,
                    crash_name=crash_name,
                )
                return

        raise Exception("no crash reporting tasks configured")

    def task(
        self,
        task_id: str,
        *,
        report_container_type: ContainerType = ContainerType.unique_reports,
        crash_name: str = "fake-crash-sample",
    ) -> None:
        """ Inject a report into the specified crash reporting task """

        task = self.onefuzz.tasks.get(task_id)
        crashes = self._get_container(task, ContainerType.crashes)
        reports = self._get_container(task, report_container_type)

        if crashes is None:
            raise Exception("task does not have a crashes container")

        if reports is None:
            raise Exception(
                "task does not have a %s container", report_container_type.name
            )

        with tempfile.TemporaryDirectory() as tempdir:
            file_path = os.path.join(tempdir, crash_name)
            with open(file_path, "w") as handle:
                handle.write("")
            self.onefuzz.containers.files.upload_file(crashes, file_path, crash_name)

        report = Report(
            input_blob=BlobRef(
                account=self._get_storage_account(crashes),
                container=crashes,
                name=crash_name,
            ),
            executable=task.config.task.target_exe,
            crash_type="fake crash report",
            crash_site="fake crash site",
            call_stack=["#0 fake", "#1 call", "#2 stack"],
            call_stack_sha256=ZERO_SHA256,
            input_sha256=EMPTY_SHA256,
            asan_log="fake asan log",
            task_id=task_id,
            job_id=task.job_id,
        )

        with tempfile.TemporaryDirectory() as tempdir:
            file_path = os.path.join(tempdir, "report.json")
            with open(file_path, "w") as handle:
                handle.write(report.json())

            self.onefuzz.containers.files.upload_file(
                reports, file_path, crash_name + ".json"
            )


class Debug(Command):
    """ Debug running jobs """

    def __init__(self, onefuzz: Any, logger: logging.Logger):
        super().__init__(onefuzz, logger)
        self.scalesets = DebugScaleset(onefuzz, logger)
        self.repro = DebugRepro(onefuzz, logger)
        self.job = DebugJob(onefuzz, logger)
        self.notification = DebugNotification(onefuzz, logger)
        self.task = DebugTask(onefuzz, logger)
