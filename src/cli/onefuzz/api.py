#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
import os
import pkgutil
import re
import subprocess  # nosec
import uuid
from enum import Enum
from shutil import which
from typing import Callable, Dict, List, Optional, Tuple, Type, TypeVar
from uuid import UUID

import semver
from memoization import cached
from onefuzztypes import (
    enums,
    events,
    models,
    primitives,
    requests,
    responses,
    webhooks,
)
from pydantic import BaseModel
from six.moves import input  # workaround for static analysis

from .__version__ import __version__
from .backend import Backend, BackendConfig, ContainerWrapper, wait
from .ssh import build_ssh_command, ssh_connect, temp_file

UUID_EXPANSION = TypeVar("UUID_EXPANSION", UUID, str)

DEFAULT = BackendConfig(
    authority="https://login.microsoftonline.com/72f988bf-86f1-41af-91ab-2d7cd011db47",
    client_id="72f1562a-8c0c-41ea-beb9-fa2b71c80134",
)

# This was generated randomly and should be preserved moving forwards
ONEFUZZ_GUID_NAMESPACE = uuid.UUID("27f25e3f-6544-4b69-b309-9b096c5a9cbc")

ONE_HOUR_IN_SECONDS = 3600

DEFAULT_LINUX_IMAGE = "Canonical:UbuntuServer:18.04-LTS:latest"
DEFAULT_WINDOWS_IMAGE = "MicrosoftWindowsDesktop:Windows-10:20h2-pro:latest"

REPRO_SSH_FORWARD = "1337:127.0.0.1:1337"

UUID_RE = r"^[a-f0-9]{8}-?[a-f0-9]{4}-?[a-f0-9]{4}-?[a-f0-9]{4}-?[a-f0-9]{12}\Z"


class PreviewFeature(Enum):
    job_templates = "job_templates"


def is_uuid(value: str) -> bool:
    return bool(re.match(UUID_RE, value))


A = TypeVar("A", bound=BaseModel)


def _wsl_path(path: str) -> str:
    if which("wslpath"):
        # security note: path should always be a temporary path constructed by
        # this library
        return (
            subprocess.check_output(["wslpath", "-w", path]).decode().strip()  # nosec
        )
    return path


def user_confirmation(message: str) -> bool:
    answer: Optional[str] = None
    while answer not in ["y", "n"]:
        answer = input(message).strip()

    if answer == "n":
        return False
    return True


class Endpoint:
    endpoint: str

    def __init__(self, onefuzz: "Onefuzz"):
        self.onefuzz = onefuzz
        self.logger = onefuzz.logger

    def _req_model(
        self,
        method: str,
        model: Type[A],
        *,
        data: Optional[BaseModel] = None,
        as_params: bool = False,
        alternate_endpoint: Optional[str] = None,
    ) -> A:
        endpoint = self.endpoint if alternate_endpoint is None else alternate_endpoint

        if as_params:
            response = self.onefuzz._backend.request(method, endpoint, params=data)
        else:
            response = self.onefuzz._backend.request(method, endpoint, json_data=data)

        return model.parse_obj(response)

    def _req_model_list(
        self,
        method: str,
        model: Type[A],
        *,
        data: Optional[BaseModel] = None,
        as_params: bool = False,
        alternate_endpoint: Optional[str] = None,
    ) -> List[A]:
        endpoint = self.endpoint if alternate_endpoint is None else alternate_endpoint

        if as_params:
            response = self.onefuzz._backend.request(method, endpoint, params=data)
        else:
            response = self.onefuzz._backend.request(method, endpoint, json_data=data)

        return [model.parse_obj(x) for x in response]

    def _disambiguate(
        self,
        name: str,
        value: str,
        check: Callable[[str], bool],
        func: Callable[[], List[str]],
    ) -> str:
        if check(value):
            return value

        self.logger.debug("expanding %s: %s", name, value)

        values = [x for x in func() if x.startswith(value)]
        if len(values) == 1:
            return values[0]

        if len(values) > 1:
            if value in values:
                return value
            raise Exception(
                "%s expands to multiple values - %s: %s"
                % (name, value, ",".join(values))
            )

        raise Exception("Unable to find %s based on prefix: %s" % (name, value))

    def _disambiguate_uuid(
        self,
        name: str,
        value: UUID_EXPANSION,
        func: Callable[[], List[str]],
    ) -> UUID:
        if isinstance(value, UUID):
            return value
        return UUID(self._disambiguate(name, value, is_uuid, func))


class Files(Endpoint):
    """Interact with files within a container"""

    endpoint = "files"

    @cached(ttl=ONE_HOUR_IN_SECONDS)
    def _get_client(self, container: primitives.Container) -> ContainerWrapper:
        sas = self.onefuzz.containers.get(container).sas_url
        return ContainerWrapper(sas)

    def list(
        self, container: primitives.Container, prefix: Optional[str] = None
    ) -> models.Files:
        """Get a list of files in a container"""
        self.logger.debug("listing files in container: %s", container)
        client = self._get_client(container)
        return models.Files(files=client.list_blobs(name_starts_with=prefix))

    def delete(self, container: primitives.Container, filename: str) -> None:
        """delete a file from a container"""
        self.logger.debug("deleting in container: %s:%s", container, filename)
        client = self._get_client(container)
        client.delete_blob(filename)

    def get(self, container: primitives.Container, filename: str) -> bytes:
        """get a file from a container"""
        self.logger.debug("getting file from container: %s:%s", container, filename)
        client = self._get_client(container)
        downloaded = client.download_blob(filename)
        return downloaded

    def upload_file(
        self,
        container: primitives.Container,
        file_path: str,
        blob_name: Optional[str] = None,
    ) -> None:
        """uploads a file to a container"""
        if not blob_name:
            # Default blob name to file basename. This means that the file data will be
            # written to the "root" of the container, if simulating a directory tree.
            blob_name = os.path.basename(file_path)

        self.logger.debug(
            "uploading file to container %s:%s (blob_name: %s)",
            container,
            file_path,
            blob_name,
        )

        client = self._get_client(container)
        client.upload_file(file_path, blob_name)

    def upload_dir(
        self, container: primitives.Container, dir_path: primitives.Directory
    ) -> None:
        """uploads a directory to a container"""

        self.logger.debug("uploading directory to container %s:%s", container, dir_path)

        client = self._get_client(container)
        client.upload_dir(dir_path)

    def download_dir(
        self, container: primitives.Container, dir_path: primitives.Directory
    ) -> None:
        """downloads a container to a directory"""

        self.logger.debug(
            "downloading container to directory %s:%s", container, dir_path
        )

        client = self._get_client(container)
        client.download_dir(dir_path)


class Versions(Endpoint):
    """Onefuzz Instance"""

    def check(self, exact: bool = False) -> str:
        """Compare API and CLI versions for compatibility"""
        versions = self.onefuzz.info.get().versions
        api_str = versions["onefuzz"].version
        cli_str = __version__
        if exact:
            result = semver.compare(api_str, cli_str) == 0
        else:
            api = semver.VersionInfo.parse(api_str)
            cli = semver.VersionInfo.parse(cli_str)
            result = (
                api.major > 0 and api.major == cli.major and api.minor >= cli.minor
            ) or (
                api.major == 0
                and api.major == cli.major
                and api.minor == cli.minor
                and api.patch >= cli.patch
            )
            if cli_str == "0.0.0" and not result:
                self.logger.warning(
                    "ignoring compatibility check as the CLI was installed "
                    "from git.  api: %s cli: %s",
                    api_str,
                    cli_str,
                )
                result = True

        if not result:
            raise Exception(
                "incompatible versions.  api: %s cli: %s" % (api_str, cli_str)
            )

        return "compatible"


class Info(Endpoint):
    """Information about the OneFuzz instance"""

    endpoint = "info"

    def get(self) -> responses.Info:
        """Get information about the OneFuzz instance"""
        self.logger.debug("getting info")
        return self._req_model("GET", responses.Info)


class Webhooks(Endpoint):
    """Interact with Webhooks"""

    endpoint = "webhooks"

    def get(self, webhook_id: UUID_EXPANSION) -> webhooks.Webhook:
        """get a webhook"""

        webhook_id_expanded = self._disambiguate_uuid(
            "webhook_id", webhook_id, lambda: [str(x.webhook_id) for x in self.list()]
        )

        self.logger.debug("getting webhook: %s", webhook_id_expanded)
        return self._req_model(
            "GET",
            webhooks.Webhook,
            data=requests.WebhookSearch(webhook_id=webhook_id_expanded),
        )

    def list(self) -> List[webhooks.Webhook]:
        """list webhooks"""

        self.logger.debug("listing webhooks")
        return self._req_model_list(
            "GET",
            webhooks.Webhook,
            data=requests.WebhookSearch(),
        )

    def create(
        self,
        name: str,
        url: str,
        event_types: List[events.EventType],
        *,
        secret_token: Optional[str] = None,
    ) -> webhooks.Webhook:
        """Create a webhook"""
        self.logger.debug("creating webhook.  name: %s", name)
        return self._req_model(
            "POST",
            webhooks.Webhook,
            data=requests.WebhookCreate(
                name=name, url=url, event_types=event_types, secret_token=secret_token
            ),
        )

    def update(
        self,
        webhook_id: UUID_EXPANSION,
        *,
        name: Optional[str] = None,
        url: Optional[str] = None,
        event_types: Optional[List[events.EventType]] = None,
        secret_token: Optional[str] = None,
    ) -> webhooks.Webhook:
        """Update a webhook"""

        webhook_id_expanded = self._disambiguate_uuid(
            "webhook_id", webhook_id, lambda: [str(x.webhook_id) for x in self.list()]
        )

        self.logger.debug("updating webhook: %s", webhook_id_expanded)
        return self._req_model(
            "PATCH",
            webhooks.Webhook,
            data=requests.WebhookUpdate(
                webhook_id=webhook_id_expanded,
                name=name,
                url=url,
                event_types=event_types,
                secret_token=secret_token,
            ),
        )

    def delete(self, webhook_id: UUID_EXPANSION) -> responses.BoolResult:
        """Delete a webhook"""

        webhook_id_expanded = self._disambiguate_uuid(
            "webhook_id", webhook_id, lambda: [str(x.webhook_id) for x in self.list()]
        )

        return self._req_model(
            "DELETE",
            responses.BoolResult,
            data=requests.WebhookGet(webhook_id=webhook_id_expanded),
        )

    def ping(self, webhook_id: UUID_EXPANSION) -> events.EventPing:
        """ping a webhook"""

        webhook_id_expanded = self._disambiguate_uuid(
            "webhook_id", webhook_id, lambda: [str(x.webhook_id) for x in self.list()]
        )

        self.logger.debug("pinging webhook: %s", webhook_id_expanded)
        return self._req_model(
            "POST",
            events.EventPing,
            data=requests.WebhookGet(webhook_id=webhook_id_expanded),
            alternate_endpoint="webhooks/ping",
        )

    def logs(self, webhook_id: UUID_EXPANSION) -> List[webhooks.WebhookMessageLog]:
        """retreive webhook event log"""

        webhook_id_expanded = self._disambiguate_uuid(
            "webhook_id", webhook_id, lambda: [str(x.webhook_id) for x in self.list()]
        )

        self.logger.debug("pinging webhook: %s", webhook_id_expanded)
        return self._req_model_list(
            "POST",
            webhooks.WebhookMessageLog,
            data=requests.WebhookGet(webhook_id=webhook_id_expanded),
            alternate_endpoint="webhooks/logs",
        )


class Containers(Endpoint):
    """Interact with Onefuzz containers"""

    endpoint = "containers"

    def __init__(self, onefuzz: "Onefuzz"):
        super().__init__(onefuzz)
        self.files = Files(onefuzz)

    def get(self, name: str) -> responses.ContainerInfo:
        """Get a fully qualified SAS URL for a container"""
        self.logger.debug("get container: %s", name)
        return self._req_model(
            "GET", responses.ContainerInfo, data=requests.ContainerGet(name=name)
        )

    def create(
        self, name: str, metadata: Optional[Dict[str, str]] = None
    ) -> responses.ContainerInfo:
        """Create a storage container"""
        self.logger.debug("create container: %s", name)
        return self._req_model(
            "POST",
            responses.ContainerInfo,
            data=requests.ContainerCreate(name=name, metadata=metadata),
        )

    def delete(self, name: str) -> responses.BoolResult:
        """Delete a storage container"""
        self.logger.debug("delete container: %s", name)
        return self._req_model(
            "DELETE", responses.BoolResult, data=requests.ContainerDelete(name=name)
        )

    def list(self) -> List[responses.ContainerInfoBase]:
        """Get a list of containers"""
        self.logger.debug("list containers")
        return self._req_model_list("GET", responses.ContainerInfoBase)

    def reset(
        self,
        *,
        container_types: Optional[
            List[enums.ContainerType]
        ] = enums.ContainerType.reset_defaults(),
        yes: bool = False,
    ) -> None:
        """
        Reset containers by container type  (NOTE: This may cause unexpected issues with existing fuzzing jobs)
        """
        if not container_types:
            return

        message = "Confirm deleting container types: %s (specify y or n): " % (
            ",".join(x.name for x in container_types)
        )
        if not yes and not user_confirmation(message):
            self.logger.warning("not deleting containers")
            return

        for container in self.list():
            if (
                container.metadata
                and "container_type" in container.metadata
                and enums.ContainerType(container.metadata["container_type"])
                in container_types
            ):
                self.logger.info("removing container: %s", container.name)
                self.delete(container.name)


class Repro(Endpoint):
    """Interact with Reproduction VMs"""

    endpoint = "repro_vms"

    def get(self, vm_id: UUID_EXPANSION) -> models.Repro:
        """get information about a Reproduction VM"""
        vm_id_expanded = self._disambiguate_uuid(
            "vm_id", vm_id, lambda: [str(x.vm_id) for x in self.list()]
        )

        self.logger.debug("get repro vm: %s", vm_id_expanded)
        return self._req_model(
            "GET", models.Repro, data=requests.ReproGet(vm_id=vm_id_expanded)
        )

    def create(
        self, container: primitives.Container, path: str, duration: int = 24
    ) -> models.Repro:
        """Create a Reproduction VM from a Crash Report"""
        self.logger.info(
            "creating repro vm: %s %s (%d hours)", container, path, duration
        )
        return self._req_model(
            "POST",
            models.Repro,
            data=models.ReproConfig(container=container, path=path, duration=duration),
        )

    def delete(self, vm_id: UUID_EXPANSION) -> models.Repro:
        """Delete a Reproduction VM"""
        vm_id_expanded = self._disambiguate_uuid(
            "vm_id", vm_id, lambda: [str(x.vm_id) for x in self.list()]
        )

        self.logger.debug("deleting repro vm: %s", vm_id_expanded)
        return self._req_model(
            "DELETE", models.Repro, data=requests.ReproGet(vm_id=vm_id_expanded)
        )

    def list(self) -> List[models.Repro]:
        """List all VMs"""
        self.logger.debug("listing repro vms")
        return self._req_model_list("GET", models.Repro, data=requests.ReproGet())

    def _dbg_linux(
        self, repro: models.Repro, debug_command: Optional[str]
    ) -> Optional[str]:
        """Launch gdb with GDB script that includes 'target remote | ssh ...'"""

        if (
            repro.auth is None
            or repro.ip is None
            or repro.state != enums.VmState.running
        ):
            raise Exception("vm setup failed: %s" % repro.state)

        with build_ssh_command(
            repro.ip, repro.auth.private_key, command="-T"
        ) as ssh_cmd:

            gdb_script = [
                "target remote | %s sudo /onefuzz/bin/repro-stdout.sh"
                % " ".join(ssh_cmd)
            ]

            if debug_command:
                gdb_script += [debug_command, "quit"]

            with temp_file("gdb.script", "\n".join(gdb_script)) as gdb_script_path:
                dbg = ["gdb", "--silent", "--command", gdb_script_path]

                if debug_command:
                    dbg += ["--batch"]

                    try:
                        # security note: dbg is built from content coming from
                        # the server, which is trusted in this context.
                        return subprocess.run(  # nosec
                            dbg, stdout=subprocess.PIPE, stderr=subprocess.STDOUT
                        ).stdout.decode(errors="ignore")
                    except subprocess.CalledProcessError as err:
                        self.logger.error(
                            "debug failed: %s", err.output.decode(errors="ignore")
                        )
                        raise err
                else:
                    # security note: dbg is built from content coming from the
                    # server, which is trusted in this context.
                    subprocess.call(dbg)  # nosec
        return None

    def _dbg_windows(
        self, repro: models.Repro, debug_command: Optional[str]
    ) -> Optional[str]:
        """Setup an SSH tunnel, then connect via CDB over SSH tunnel"""

        if (
            repro.auth is None
            or repro.ip is None
            or repro.state != enums.VmState.running
        ):
            raise Exception("vm setup failed: %s" % repro.state)

        bind_all = which("wslpath") is not None and repro.os == enums.OS.windows
        proxy = "*:" + REPRO_SSH_FORWARD if bind_all else REPRO_SSH_FORWARD
        with ssh_connect(repro.ip, repro.auth.private_key, proxy=proxy):
            dbg = ["cdb.exe", "-remote", "tcp:port=1337,server=localhost"]
            if debug_command:
                dbg_script = [debug_command, "qq"]
                with temp_file("db.script", "\r\n".join(dbg_script)) as dbg_script_path:
                    dbg += ["-cf", _wsl_path(dbg_script_path)]

                    logging.debug("launching: %s", dbg)
                    try:
                        # security note: dbg is built from content coming from the server,
                        # which is trusted in this context.
                        return subprocess.run(  # nosec
                            dbg, stdout=subprocess.PIPE, stderr=subprocess.STDOUT
                        ).stdout.decode(errors="ignore")
                    except subprocess.CalledProcessError as err:
                        self.logger.error(
                            "debug failed: %s", err.output.decode(errors="ignore")
                        )
                        raise err
            else:
                logging.debug("launching: %s", dbg)
                # security note:  dbg is built from content coming from the
                # server, which is trusted in this context.
                subprocess.call(dbg)  # nosec

        return None

    def connect(
        self,
        vm_id: UUID_EXPANSION,
        delete_after_use: bool = False,
        debug_command: Optional[str] = None,
    ) -> Optional[str]:
        """Connect to an existing Reproduction VM"""

        self.logger.info("connecting to reproduction VM: %s", vm_id)

        if which("ssh") is None:
            raise Exception("unable to find ssh")

        def missing_os() -> Tuple[bool, str, models.Repro]:
            repro = self.get(vm_id)
            return (
                repro.os is not None,
                "waiting for os determination",
                repro,
            )

        repro = wait(missing_os)

        if repro.os == enums.OS.windows:
            if which("cdb.exe") is None:
                raise Exception("unable to find cdb.exe")
        if repro.os == enums.OS.linux:
            if which("gdb") is None:
                raise Exception("unable to find gdb")

        def func() -> Tuple[bool, str, models.Repro]:
            repro = self.get(vm_id)
            state = repro.state
            return (
                repro.auth is not None
                and repro.ip is not None
                and state not in [enums.VmState.init, enums.VmState.extensions_launch],
                "launching reproducing vm.  current state: %s" % state,
                repro,
            )

        repro = wait(func)

        result: Optional[str] = None

        if repro.os == enums.OS.windows:
            result = self._dbg_windows(repro, debug_command)
        elif repro.os == enums.OS.linux:
            result = self._dbg_linux(repro, debug_command)
        else:
            raise NotImplementedError

        if delete_after_use:
            self.logger.debug("deleting vm %s", repro.vm_id)
            self.delete(repro.vm_id)

        return result

    def create_and_connect(
        self,
        container: primitives.Container,
        path: str,
        duration: int = 24,
        delete_after_use: bool = False,
        debug_command: Optional[str] = None,
    ) -> Optional[str]:
        """Create and connect to a Reproduction VM"""
        repro = self.create(container, path, duration=duration)
        return self.connect(
            repro.vm_id, delete_after_use=delete_after_use, debug_command=debug_command
        )


class Notifications(Endpoint):
    """Interact with models.Notifications"""

    endpoint = "notifications"

    def create(
        self, container: primitives.Container, config: models.NotificationConfig
    ) -> models.Notification:
        """Create a notification based on a config file"""

        config = requests.NotificationCreate(container=container, config=config.config)
        return self._req_model("POST", models.Notification, data=config)

    def create_teams(
        self, container: primitives.Container, url: str
    ) -> models.Notification:
        """Create a Teams notification integration"""

        self.logger.debug("create teams notification integration: %s", container)

        config = models.NotificationConfig(config=models.TeamsTemplate(url=url))
        return self.create(container, config)

    def create_ado(
        self,
        container: primitives.Container,
        project: str,
        base_url: str,
        auth_token: str,
        work_item_type: str,
        unique_fields: List[str],
        comment: Optional[str] = None,
        fields: Optional[Dict[str, str]] = None,
        on_dup_increment: Optional[List[str]] = None,
        on_dup_comment: Optional[str] = None,
        on_dup_set_state: Optional[Dict[str, str]] = None,
        on_dup_fields: Optional[Dict[str, str]] = None,
    ) -> models.Notification:
        """Create an Azure DevOps notification integration"""

        self.logger.debug("create ado notification integration: %s", container)

        entry = models.NotificationConfig(
            config=models.ADOTemplate(
                base_url=base_url,
                auth_token=auth_token,
                project=project,
                type=work_item_type,
                comment=comment,
                unique_fields=unique_fields,
                ado_fields=fields,
                on_duplicate=models.ADODuplicateTemplate(
                    increment=on_dup_increment or [],
                    comment=on_dup_comment,
                    ado_fields=on_dup_fields or {},
                    set_state=on_dup_set_state or {},
                ),
            ),
        )
        return self.create(container, entry)

    def delete(self, notification_id: UUID_EXPANSION) -> models.Notification:
        """Delete a notification integration"""

        notification_id_expanded = self._disambiguate_uuid(
            "notification_id",
            notification_id,
            lambda: [str(x.notification_id) for x in self.list()],
        )

        self.logger.debug(
            "create notification integration: %s",
            notification_id_expanded,
        )
        return self._req_model(
            "DELETE",
            models.Notification,
            data=requests.NotificationGet(notification_id=notification_id_expanded),
        )

    def list(self) -> List[models.Notification]:
        """List notification integrations"""

        self.logger.debug("listing notification integrations")
        return self._req_model_list("GET", models.Notification)


class Tasks(Endpoint):
    """Interact with tasks"""

    endpoint = "tasks"

    def delete(self, task_id: UUID_EXPANSION) -> models.Task:
        """Stop an individual task"""

        task_id_expanded = self._disambiguate_uuid(
            "task_id", task_id, lambda: [str(x.task_id) for x in self.list()]
        )

        self.logger.debug("delete task: %s", task_id_expanded)

        return self._req_model(
            "DELETE", models.Task, data=requests.TaskGet(task_id=task_id_expanded)
        )

    def get(self, task_id: UUID_EXPANSION) -> models.Task:
        """Get information about a task"""
        task_id_expanded = self._disambiguate_uuid(
            "task_id", task_id, lambda: [str(x.task_id) for x in self.list()]
        )

        self.logger.debug("get task: %s", task_id_expanded)

        return self._req_model(
            "GET", models.Task, data=requests.TaskGet(task_id=task_id_expanded)
        )

    def create_with_config(self, config: models.TaskConfig) -> models.Task:
        """Create a Task using TaskConfig"""

        return self._req_model("POST", models.Task, data=config)

    def create(
        self,
        job_id: UUID_EXPANSION,
        task_type: enums.TaskType,
        target_exe: str,
        containers: List[Tuple[enums.ContainerType, primitives.Container]],
        *,
        analyzer_env: Optional[Dict[str, str]] = None,
        analyzer_exe: Optional[str] = None,
        analyzer_options: Optional[List[str]] = None,
        check_asan_log: bool = False,
        check_debugger: bool = True,
        check_retry_count: Optional[int] = None,
        check_fuzzer_help: Optional[bool] = None,
        expect_crash_on_failure: Optional[bool] = None,
        debug: Optional[List[enums.TaskDebugFlag]] = None,
        duration: int = 24,
        ensemble_sync_delay: Optional[int] = None,
        generator_exe: Optional[str] = None,
        generator_options: Optional[List[str]] = None,
        pool_name: primitives.PoolName,
        prereq_tasks: Optional[List[UUID]] = None,
        reboot_after_setup: bool = False,
        rename_output: bool = False,
        stats_file: Optional[str] = None,
        stats_format: Optional[enums.StatsFormat] = None,
        supervisor_env: Optional[Dict[str, str]] = None,
        supervisor_exe: Optional[str] = None,
        supervisor_input_marker: Optional[str] = None,
        supervisor_options: Optional[List[str]] = None,
        tags: Optional[Dict[str, str]] = None,
        task_wait_for_files: Optional[enums.ContainerType] = None,
        target_env: Optional[Dict[str, str]] = None,
        target_options: Optional[List[str]] = None,
        target_options_merge: bool = False,
        target_timeout: Optional[int] = None,
        target_workers: Optional[int] = None,
        vm_count: int = 1,
        preserve_existing_outputs: bool = False,
        colocate: bool = False,
        report_list: Optional[List[str]] = None,
        minimized_stack_depth: Optional[int] = None,
    ) -> models.Task:
        """
        Create a task

        :param bool ensemble_sync_delay: Specify duration between
            syncing inputs during ensemble fuzzing (0 to disable).
        """

        self.logger.debug("creating task: %s", task_type)

        job_id_expanded = self._disambiguate_uuid(
            "job_id",
            job_id,
            lambda: [str(x.job_id) for x in self.onefuzz.jobs.list()],
        )

        if tags is None:
            tags = {}

        containers_submit = []
        for (container_type, container) in containers:
            containers_submit.append(
                models.TaskContainers(name=container, type=container_type)
            )

        config = models.TaskConfig(
            containers=containers_submit,
            debug=debug,
            job_id=job_id_expanded,
            pool=models.TaskPool(count=vm_count, pool_name=pool_name),
            prereq_tasks=prereq_tasks,
            tags=tags,
            colocate=colocate,
            task=models.TaskDetails(
                analyzer_env=analyzer_env,
                analyzer_exe=analyzer_exe,
                analyzer_options=analyzer_options,
                check_asan_log=check_asan_log,
                check_debugger=check_debugger,
                check_retry_count=check_retry_count,
                check_fuzzer_help=check_fuzzer_help,
                expect_crash_on_failure=expect_crash_on_failure,
                duration=duration,
                ensemble_sync_delay=ensemble_sync_delay,
                generator_exe=generator_exe,
                generator_options=generator_options,
                reboot_after_setup=reboot_after_setup,
                rename_output=rename_output,
                stats_file=stats_file,
                stats_format=stats_format,
                supervisor_env=supervisor_env,
                supervisor_exe=supervisor_exe,
                supervisor_input_marker=supervisor_input_marker,
                supervisor_options=supervisor_options,
                target_env=target_env,
                target_exe=target_exe,
                target_options=target_options,
                target_options_merge=target_options_merge,
                target_timeout=target_timeout,
                target_workers=target_workers,
                type=task_type,
                wait_for_files=task_wait_for_files,
                report_list=report_list,
                preserve_existing_outputs=preserve_existing_outputs,
                minimized_stack_depth=minimized_stack_depth,
            ),
        )

        return self.create_with_config(config)

    def list(
        self,
        job_id: Optional[UUID_EXPANSION] = None,
        state: Optional[List[enums.TaskState]] = enums.TaskState.available(),
    ) -> List[models.Task]:
        """Get information about all tasks"""
        self.logger.debug("list tasks")
        job_id_expanded: Optional[UUID] = None

        if job_id is not None:
            job_id_expanded = self._disambiguate_uuid(
                "job_id",
                job_id,
                lambda: [str(x.job_id) for x in self.onefuzz.jobs.list()],
            )

        return self._req_model_list(
            "GET",
            models.Task,
            data=requests.TaskSearch(job_id=job_id_expanded, state=state),
        )


class JobContainers(Endpoint):
    """Interact with Containers used within tasks in a Job"""

    endpoint = "jobs"

    def list(
        self,
        job_id: UUID_EXPANSION,
        container_type: Optional[
            enums.ContainerType
        ] = enums.ContainerType.unique_reports,
    ) -> Dict[str, List[str]]:
        """
        List the files for all of the containers of a given container type
        for the specified job
        """
        containers = set()
        tasks = self.onefuzz.tasks.list(job_id=job_id, state=[])
        for task in tasks:
            containers.update(
                set(x.name for x in task.config.containers if x.type == container_type)
            )

        results: Dict[str, List[str]] = {}
        for container in containers:
            results[container] = self.onefuzz.containers.files.list(container).files
        return results


class JobTasks(Endpoint):
    """Interact with tasks within a job"""

    endpoint = "jobs"

    def list(self, job_id: UUID_EXPANSION) -> List[models.Task]:
        """List all of the tasks for a given job"""
        return self.onefuzz.tasks.list(job_id=job_id, state=[])


class Jobs(Endpoint):
    """Interact with Jobs"""

    endpoint = "jobs"

    def __init__(self, onefuzz: "Onefuzz"):
        super().__init__(onefuzz)
        self.containers = JobContainers(onefuzz)
        self.tasks = JobTasks(onefuzz)

    def delete(self, job_id: UUID_EXPANSION) -> models.Job:

        """Stop a job and all tasks that make up a job"""
        job_id_expanded = self._disambiguate_uuid(
            "job_id", job_id, lambda: [str(x.job_id) for x in self.list()]
        )

        self.logger.debug("delete job: %s", job_id_expanded)
        return self._req_model(
            "DELETE", models.Job, data=requests.JobGet(job_id=job_id_expanded)
        )

    def get(self, job_id: UUID_EXPANSION) -> models.Job:
        """Get information about a specific job"""
        job_id_expanded = self._disambiguate_uuid(
            "job_id", job_id, lambda: [str(x.job_id) for x in self.list()]
        )
        self.logger.debug("get job: %s", job_id_expanded)
        job = self._req_model(
            "GET", models.Job, data=requests.JobGet(job_id=job_id_expanded)
        )
        return job

    def create_with_config(self, config: models.JobConfig) -> models.Job:
        """Create a job"""
        self.logger.debug(
            "create job: project:%s name:%s build:%s",
            config.project,
            config.name,
            config.build,
        )
        return self._req_model(
            "POST",
            models.Job,
            data=config,
        )

    def create(
        self, project: str, name: str, build: str, duration: int = 24
    ) -> models.Job:
        """Create a job"""
        return self.create_with_config(
            models.JobConfig(project=project, name=name, build=build, duration=duration)
        )

    def list(
        self,
        job_state: Optional[List[enums.JobState]] = enums.JobState.available(),
    ) -> List[models.Job]:
        """Get information about all jobs"""
        self.logger.debug("list jobs")
        return self._req_model_list(
            "GET", models.Job, data=requests.JobSearch(state=job_state)
        )


class Pool(Endpoint):
    """Interact with worker pools"""

    endpoint = "pool"

    def create(
        self,
        name: str,
        os: enums.OS,
        client_id: Optional[UUID] = None,
        *,
        unmanaged: bool = False,
        arch: enums.Architecture = enums.Architecture.x86_64,
    ) -> models.Pool:
        """
        Create a worker pool

        :param str name: Name of the worker-pool
        """
        self.logger.debug("create worker pool")
        managed = not unmanaged

        return self._req_model(
            "POST",
            models.Pool,
            data=requests.PoolCreate(
                name=name, os=os, arch=arch, managed=managed, client_id=client_id
            ),
        )

    def get_config(self, pool_name: primitives.PoolName) -> models.AgentConfig:
        """Get the agent configuration for the pool"""

        pool = self.get(pool_name)

        if pool.config is None:
            raise Exception("Missing AgentConfig in response")

        config = pool.config
        config.client_credentials = models.ClientCredentials(  # nosec - bandit consider this a hard coded password
            client_id=pool.client_id,
            client_secret="<client secret>",
        )

        return config

    def shutdown(self, name: str, *, now: bool = False) -> responses.BoolResult:
        expanded_name = self._disambiguate(
            "name", name, lambda x: False, lambda: [x.name for x in self.list()]
        )

        self.logger.debug("shutdown worker pool: %s (now: %s)", expanded_name, now)
        return self._req_model(
            "DELETE",
            responses.BoolResult,
            data=requests.PoolStop(name=expanded_name, now=now),
        )

    def get(self, name: str) -> models.Pool:
        self.logger.debug("get details on a specific pool")
        expanded_name = self._disambiguate(
            "pool name", name, lambda x: False, lambda: [x.name for x in self.list()]
        )

        return self._req_model(
            "GET", models.Pool, data=requests.PoolSearch(name=expanded_name)
        )

    def list(
        self, *, state: Optional[List[enums.PoolState]] = None
    ) -> List[models.Pool]:
        self.logger.debug("list worker pools")
        return self._req_model_list(
            "GET", models.Pool, data=requests.PoolSearch(state=state)
        )


class Node(Endpoint):
    """Interact with nodes"""

    endpoint = "node"

    def get(self, machine_id: UUID_EXPANSION) -> models.Node:
        self.logger.debug("get node: %s", machine_id)
        machine_id_expanded = self._disambiguate_uuid(
            "machine_id",
            machine_id,
            lambda: [str(x.machine_id) for x in self.list()],
        )

        return self._req_model(
            "GET", models.Node, data=requests.NodeGet(machine_id=machine_id_expanded)
        )

    def halt(self, machine_id: UUID_EXPANSION) -> responses.BoolResult:
        self.logger.debug("halt node: %s", machine_id)
        machine_id_expanded = self._disambiguate_uuid(
            "machine_id",
            machine_id,
            lambda: [str(x.machine_id) for x in self.list()],
        )

        return self._req_model(
            "DELETE",
            responses.BoolResult,
            data=requests.NodeGet(machine_id=machine_id_expanded),
        )

    def reimage(self, machine_id: UUID_EXPANSION) -> responses.BoolResult:
        self.logger.debug("reimage node: %s", machine_id)
        machine_id_expanded = self._disambiguate_uuid(
            "machine_id",
            machine_id,
            lambda: [str(x.machine_id) for x in self.list()],
        )

        return self._req_model(
            "PATCH",
            responses.BoolResult,
            data=requests.NodeGet(machine_id=machine_id_expanded),
        )

    def update(
        self,
        machine_id: UUID_EXPANSION,
        *,
        debug_keep_node: Optional[bool] = None,
    ) -> responses.BoolResult:
        self.logger.debug("update node: %s", machine_id)
        machine_id_expanded = self._disambiguate_uuid(
            "machine_id",
            machine_id,
            lambda: [str(x.machine_id) for x in self.list()],
        )

        return self._req_model(
            "POST",
            responses.BoolResult,
            data=requests.NodeUpdate(
                machine_id=machine_id_expanded,
                debug_keep_node=debug_keep_node,
            ),
        )

    def list(
        self,
        *,
        state: Optional[List[enums.NodeState]] = None,
        scaleset_id: Optional[UUID_EXPANSION] = None,
        pool_name: Optional[primitives.PoolName] = None,
    ) -> List[models.Node]:
        self.logger.debug("list nodes")
        scaleset_id_expanded: Optional[UUID] = None

        if pool_name is not None:
            pool_name = primitives.PoolName(
                self._disambiguate(
                    "name",
                    str(pool_name),
                    lambda x: False,
                    lambda: [x.name for x in self.onefuzz.pools.list()],
                )
            )

        if scaleset_id is not None:
            scaleset_id_expanded = self._disambiguate_uuid(
                "scaleset_id",
                scaleset_id,
                lambda: [str(x.scaleset_id) for x in self.onefuzz.scalesets.list()],
            )

        return self._req_model_list(
            "GET",
            models.Node,
            data=requests.NodeSearch(
                scaleset_id=scaleset_id_expanded, state=state, pool_name=pool_name
            ),
        )

    def add_ssh_key(
        self, machine_id: UUID_EXPANSION, *, public_key: str
    ) -> responses.BoolResult:
        self.logger.debug("add ssh public key to node: %s", machine_id)
        machine_id_expanded = self._disambiguate_uuid(
            "machine_id",
            machine_id,
            lambda: [str(x.machine_id) for x in self.list()],
        )

        return self._req_model(
            "POST",
            responses.BoolResult,
            data=requests.NodeAddSshKey(
                machine_id=machine_id_expanded,
                public_key=public_key,
            ),
            alternate_endpoint="node/add_ssh_key",
        )


class Scaleset(Endpoint):
    """Interact with managed scaleset pools"""

    endpoint = "scaleset"

    def _expand_scaleset_machine(
        self,
        scaleset_id: UUID_EXPANSION,
        machine_id: UUID_EXPANSION,
        *,
        include_auth: bool = False,
    ) -> Tuple[models.Scaleset, UUID]:
        scaleset = self.get(scaleset_id, include_auth=include_auth)

        if scaleset.nodes is None:
            raise Exception("no nodes defined in scaleset")
        nodes = scaleset.nodes

        machine_id_expanded = self._disambiguate_uuid(
            "machine_id", machine_id, lambda: [str(x.machine_id) for x in nodes]
        )
        return (scaleset, machine_id_expanded)

    def create(
        self,
        pool_name: primitives.PoolName,
        size: int,
        *,
        image: Optional[str] = None,
        vm_sku: Optional[str] = "Standard_D2s_v3",
        region: Optional[primitives.Region] = None,
        spot_instances: bool = False,
        ephemeral_os_disks: bool = False,
        tags: Optional[Dict[str, str]] = None,
    ) -> models.Scaleset:
        self.logger.debug("create scaleset")

        if tags is None:
            tags = {}

        if image is None:
            pool = self.onefuzz.pools.get(pool_name)
            if pool.os == enums.OS.linux:
                image = DEFAULT_LINUX_IMAGE
            elif pool.os == enums.OS.windows:
                image = DEFAULT_WINDOWS_IMAGE
            else:
                raise NotImplementedError

        return self._req_model(
            "POST",
            models.Scaleset,
            data=requests.ScalesetCreate(
                pool_name=pool_name,
                vm_sku=vm_sku,
                image=image,
                region=region,
                size=size,
                spot_instances=spot_instances,
                ephemeral_os_disks=ephemeral_os_disks,
                tags=tags,
            ),
        )

    def shutdown(
        self, scaleset_id: UUID_EXPANSION, *, now: bool = False
    ) -> responses.BoolResult:
        scaleset_id_expanded = self._disambiguate_uuid(
            "scaleset_id",
            scaleset_id,
            lambda: [str(x.scaleset_id) for x in self.list()],
        )

        self.logger.debug("shutdown scaleset: %s (now: %s)", scaleset_id_expanded, now)
        return self._req_model(
            "DELETE",
            responses.BoolResult,
            data=requests.ScalesetStop(scaleset_id=scaleset_id_expanded, now=now),
        )

    def get(
        self, scaleset_id: UUID_EXPANSION, *, include_auth: bool = False
    ) -> models.Scaleset:
        self.logger.debug("get scaleset: %s", scaleset_id)
        scaleset_id_expanded = self._disambiguate_uuid(
            "scaleset_id",
            scaleset_id,
            lambda: [str(x.scaleset_id) for x in self.list()],
        )

        return self._req_model(
            "GET",
            models.Scaleset,
            data=requests.ScalesetSearch(
                scaleset_id=scaleset_id_expanded, include_auth=include_auth
            ),
        )

    def update(
        self, scaleset_id: UUID_EXPANSION, *, size: Optional[int] = None
    ) -> models.Scaleset:
        self.logger.debug("update scaleset: %s", scaleset_id)
        scaleset_id_expanded = self._disambiguate_uuid(
            "scaleset_id",
            scaleset_id,
            lambda: [str(x.scaleset_id) for x in self.list()],
        )

        return self._req_model(
            "PATCH",
            models.Scaleset,
            data=requests.ScalesetUpdate(scaleset_id=scaleset_id_expanded, size=size),
        )

    def list(
        self,
        *,
        state: Optional[List[enums.ScalesetState]] = None,
    ) -> List[models.Scaleset]:
        self.logger.debug("list scalesets")
        return self._req_model_list(
            "GET", models.Scaleset, data=requests.ScalesetSearch(state=state)
        )


class ScalesetProxy(Endpoint):
    """Interact with Scaleset Proxies (NOTE: This API is unstable)"""

    endpoint = "proxy"

    def delete(
        self,
        scaleset_id: UUID_EXPANSION,
        machine_id: UUID_EXPANSION,
        *,
        dst_port: Optional[int] = None,
    ) -> responses.BoolResult:
        """Stop a proxy node"""

        (
            scaleset,
            machine_id_expanded,
        ) = self.onefuzz.scalesets._expand_scaleset_machine(scaleset_id, machine_id)

        self.logger.debug(
            "delete proxy: %s:%d %d",
            scaleset.scaleset_id,
            machine_id_expanded,
            dst_port,
        )
        return self._req_model(
            "DELETE",
            responses.BoolResult,
            data=requests.ProxyDelete(
                scaleset_id=scaleset.scaleset_id,
                machine_id=machine_id_expanded,
                dst_port=dst_port,
            ),
        )

    def reset(self, region: primitives.Region) -> responses.BoolResult:
        """Reset the proxy for an existing region"""

        return self._req_model(
            "PATCH", responses.BoolResult, data=requests.ProxyReset(region=region)
        )

    def get(
        self, scaleset_id: UUID_EXPANSION, machine_id: UUID_EXPANSION, dst_port: int
    ) -> responses.ProxyGetResult:
        """Get information about a specific job"""
        (
            scaleset,
            machine_id_expanded,
        ) = self.onefuzz.scalesets._expand_scaleset_machine(scaleset_id, machine_id)

        self.logger.debug(
            "get proxy: %s:%d:%d", scaleset.scaleset_id, machine_id_expanded, dst_port
        )
        proxy = self._req_model(
            "GET",
            responses.ProxyGetResult,
            data=requests.ProxyGet(
                scaleset_id=scaleset.scaleset_id,
                machine_id=machine_id_expanded,
                dst_port=dst_port,
            ),
        )
        return proxy

    def create(
        self,
        scaleset_id: UUID_EXPANSION,
        machine_id: UUID_EXPANSION,
        dst_port: int,
        *,
        duration: Optional[int] = 1,
    ) -> responses.ProxyGetResult:
        """Create a proxy"""
        (
            scaleset,
            machine_id_expanded,
        ) = self.onefuzz.scalesets._expand_scaleset_machine(scaleset_id, machine_id)

        self.logger.debug(
            "create proxy: %s:%s %d",
            scaleset.scaleset_id,
            machine_id_expanded,
            dst_port,
        )
        return self._req_model(
            "POST",
            responses.ProxyGetResult,
            data=requests.ProxyCreate(
                scaleset_id=scaleset.scaleset_id,
                machine_id=machine_id_expanded,
                dst_port=dst_port,
                duration=duration,
            ),
        )


class Command:
    def __init__(self, onefuzz: "Onefuzz", logger: logging.Logger):
        self.onefuzz = onefuzz
        self.logger = logger


class Utils(Command):
    def namespaced_guid(
        self,
        project: str,
        name: Optional[str] = None,
        build: Optional[str] = None,
        platform: Optional[str] = None,
    ) -> uuid.UUID:
        identifiers = [project]
        if name is not None:
            identifiers.append(name)
        if build is not None:
            identifiers.append(build)
        if platform is not None:
            identifiers.append(platform)
        return uuid.uuid5(ONEFUZZ_GUID_NAMESPACE, ":".join(identifiers))


class Onefuzz:
    def __init__(
        self, config_path: Optional[str] = None, token_path: Optional[str] = None
    ) -> None:
        self.logger = logging.getLogger("onefuzz")
        self._backend = Backend(
            config=DEFAULT, config_path=config_path, token_path=token_path
        )
        self.containers = Containers(self)
        self.repro = Repro(self)
        self.notifications = Notifications(self)
        self.tasks = Tasks(self)
        self.jobs = Jobs(self)
        self.versions = Versions(self)
        self.info = Info(self)
        self.scaleset_proxy = ScalesetProxy(self)
        self.pools = Pool(self)
        self.scalesets = Scaleset(self)
        self.nodes = Node(self)
        self.webhooks = Webhooks(self)

        if self._backend.is_feature_enabled(PreviewFeature.job_templates.name):
            self.job_templates = JobTemplates(self)

        # these are externally developed cli modules
        self.template = Template(self, self.logger)
        self.debug = Debug(self, self.logger)
        self.status = Status(self, self.logger)
        self.utils = Utils(self, self.logger)

        self.__setup__()

    def __setup__(
        self,
        endpoint: Optional[str] = None,
        client_id: Optional[str] = None,
        authority: Optional[str] = None,
        tenant_domain: Optional[str] = None,
    ) -> None:

        if endpoint:
            self._backend.config.endpoint = endpoint
        if authority is not None:
            self._backend.config.authority = authority
        if client_id is not None:
            self._backend.config.client_id = client_id
        if tenant_domain is not None:
            self._backend.config.tenant_domain = tenant_domain

        if self._backend.is_feature_enabled(PreviewFeature.job_templates.name):
            self.job_templates._load_cache()

    def licenses(self) -> object:
        """Return third-party licenses used by this package"""
        data = pkgutil.get_data("onefuzz", "data/licenses.json")
        if data is None:
            raise Exception("missing licenses.json")
        return json.loads(data)

    def privacy_statement(self) -> bytes:
        """Return OneFuzz privacy statement"""
        data = pkgutil.get_data("onefuzz", "data/privacy.txt")
        if data is None:
            raise Exception("missing licenses.json")
        return data

    def logout(self) -> None:
        """Logout of Onefuzz"""
        self.logger.debug("logout")

        self._backend.logout()

    def login(self) -> str:
        """Login to Onefuzz"""

        # Rather than interacting MSAL directly, call a simple API which
        # actuates the login process
        self.info.get()

        # TODO: once job templates are out of preview, this should be enabled
        if self._backend.is_feature_enabled(PreviewFeature.job_templates.name):
            self.job_templates.refresh()
        return "succeeded"

    def config(
        self,
        endpoint: Optional[str] = None,
        authority: Optional[str] = None,
        client_id: Optional[str] = None,
        client_secret: Optional[str] = None,
        enable_feature: Optional[PreviewFeature] = None,
        tenant_domain: Optional[str] = None,
        reset: Optional[bool] = None,
    ) -> BackendConfig:
        """Configure onefuzz CLI"""
        self.logger.debug("set config")

        if reset:
            self._backend.config = BackendConfig(authority="", client_id="")

        if endpoint is not None:
            # The normal path for calling the API always uses the oauth2 workflow,
            # which the devicelogin can take upwards of 15 minutes to fail in
            # error cases.
            #
            # This check only happens on setting the configuration, as checking the
            # viability of the service on every call is prohibitively expensive.
            verify = self._backend.session.request("GET", endpoint)
            if verify.status_code != 401:
                self.logger.warning(
                    "This could be an invalid OneFuzz API endpoint: "
                    "Missing HTTP Authentication"
                )
            self._backend.config.endpoint = endpoint
        if authority is not None:
            self._backend.config.authority = authority
        if client_id is not None:
            self._backend.config.client_id = client_id
        if client_secret is not None:
            self._backend.config.client_secret = client_secret
        if enable_feature:
            self._backend.enable_feature(enable_feature.name)
        if tenant_domain is not None:
            self._backend.config.tenant_domain = tenant_domain
        self._backend.app = None
        self._backend.save_config()

        data = self._backend.config.copy(deep=True)
        if data.client_secret is not None:
            # replace existing secrets with "*** for user display
            data.client_secret = "***"  # nosec

        if not data.endpoint:
            self.logger.warning("endpoint not configured yet")

        return data

    def _delete_components(
        self,
        *,
        containers: bool = False,
        jobs: bool = False,
        notifications: bool = False,
        pools: bool = False,
        repros: bool = False,
        scalesets: bool = False,
        tasks: bool = False,
        webhooks: bool = False,
    ) -> None:
        if jobs:
            for job in self.jobs.list():
                self.logger.info("stopping job %s", job.job_id)
                self.jobs.delete(job.job_id)

        if tasks:
            for task in self.tasks.list():
                self.logger.info("stopping task %s", task.task_id)
                self.tasks.delete(task.task_id)

        if notifications:
            for notification in self.notifications.list():
                self.logger.info(
                    "stopping notification %s", notification.notification_id
                )
                self.notifications.delete(notification.notification_id)

        if repros:
            for vm in self.repro.list():
                self.repro.delete(str(vm.vm_id))

        if pools:
            for pool in self.pools.list():
                self.logger.info("stopping pool: %s", pool.name)
                self.pools.shutdown(pool.name, now=True)

        if scalesets:
            for scaleset in self.scalesets.list():
                self.logger.info("stopping scaleset: %s", scaleset.scaleset_id)
                self.scalesets.shutdown(scaleset.scaleset_id, now=True)

        if containers:
            self.containers.reset(yes=True)

        if webhooks:
            for webhook in self.webhooks.list():
                self.logger.info("removing webhook: %s", webhook.webhook_id)
                self.webhooks.delete(webhook.webhook_id)

    def reset(
        self,
        *,
        containers: bool = False,
        everything: bool = False,
        jobs: bool = False,
        notifications: bool = False,
        pools: bool = False,
        repros: bool = False,
        scalesets: bool = False,
        tasks: bool = False,
        webhooks: bool = False,
        yes: bool = False,
    ) -> None:
        """
        Resets onefuzz. Stops all jobs, notifications, and repro VMs.
        Specifying 'everything' will delete all containers, pools, and managed
        scalesets.

        :param bool containers: Delete all the containers.
        :param bool everything: Delete all containers, pools and managed scalesets.
        :param bool jobs: Stop all jobs.
        :param bool notifications: Stop all notifications.
        :param bool pools: Delete all pools.
        :param bool repros: Delete all repro vms.
        :param bool scalesets: Delete all managed scalesets.
        :param bool tasks: Stop all tasks.
        :param bool webhooks: Stop all webhooks.
        :param bool yes: Ignoring to specify "y" in prompt.
        """

        if everything:
            (
                containers,
                jobs,
                pools,
                notifications,
                repros,
                scalesets,
                tasks,
                webhooks,
            ) = (
                True,
                True,
                True,
                True,
                True,
                True,
                True,
                True,
            )
        elif not any(
            [containers, jobs, pools, notifications, repros, scalesets, tasks]
        ):
            jobs, notifications, repros, tasks = True, True, True, True

        if containers and not (tasks or jobs):
            raise Exception("Resetting containers requires resetting jobs or tasks")

        to_delete = []
        argument_str = {
            "jobs",
            "tasks",
            "notifications",
            "pools",
            "scalesets",
            "repros",
            "containers",
            "webhooks",
        }
        for k, v in locals().items():
            if k in argument_str and v:
                to_delete.append(k)
        message = "Confirm stopping %s (specify y or n): " % (
            ", ".join(sorted(to_delete))
        )
        if not yes and not user_confirmation(message):
            self.logger.warning("not resetting")
            return

        self._delete_components(
            containers=containers,
            jobs=jobs,
            notifications=notifications,
            pools=pools,
            repros=repros,
            scalesets=scalesets,
            tasks=tasks,
            webhooks=webhooks,
        )

    def _warn_preview(self, feature: PreviewFeature) -> None:
        self.logger.warning(
            "%s are a preview-feature and may change in an upcoming release",
            feature.name,
        )


from .debug import Debug  # noqa: E402
from .job_templates.main import JobTemplates  # noqa: E402
from .status.cmd import Status  # noqa: E402
from .template import Template  # noqa: E402
