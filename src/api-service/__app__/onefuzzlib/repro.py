#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from datetime import datetime, timedelta
from typing import List, Optional, Tuple, Union

from azure.mgmt.compute.models import VirtualMachine
from onefuzztypes.enums import OS, ContainerType, ErrorCode, VmState
from onefuzztypes.models import Error, NetworkSecurityGroupConfig
from onefuzztypes.models import Repro as BASE_REPRO
from onefuzztypes.models import ReproConfig, TaskVm, UserInfo
from onefuzztypes.primitives import Container

from .azure.auth import build_auth
from .azure.containers import save_blob
from .azure.creds import get_base_region
from .azure.ip import get_public_ip
from .azure.nsg import NSG
from .azure.storage import StorageType
from .azure.vm import VM
from .extension import repro_extensions
from .orm import ORMMixin, QueryFilter
from .reports import get_report
from .tasks.main import Task

DEFAULT_OS = {
    OS.linux: "Canonical:UbuntuServer:18.04-LTS:latest",
    OS.windows: "MicrosoftWindowsDesktop:Windows-10:20h2-pro:latest",
}

DEFAULT_SKU = "Standard_DS1_v2"


class Repro(BASE_REPRO, ORMMixin):
    def set_error(self, error: Error) -> None:
        logging.error(
            "repro failed: vm_id: %s task_id: %s: error: %s",
            self.vm_id,
            self.task_id,
            error,
        )
        self.error = error
        self.state = VmState.stopping
        self.save()

    def get_vm(self) -> VM:
        task = Task.get_by_task_id(self.task_id)
        if isinstance(task, Error):
            raise Exception("previously existing task missing: %s" % self.task_id)

        vm_config = task.get_repro_vm_config()
        if vm_config is None:
            # if using a pool without any scalesets defined yet, use reasonable defaults
            if task.os not in DEFAULT_OS:
                raise NotImplementedError("unsupported OS for repro %s" % task.os)

            vm_config = TaskVm(
                region=get_base_region(), sku=DEFAULT_SKU, image=DEFAULT_OS[task.os]
            )

        if self.auth is None:
            raise Exception("missing auth")

        return VM(
            name=self.vm_id,
            region=vm_config.region,
            sku=vm_config.sku,
            image=vm_config.image,
            auth=self.auth,
        )

    def init(self) -> None:
        vm = self.get_vm()
        vm_data = vm.get()
        if vm_data:
            if vm_data.provisioning_state == "Failed":
                self.set_failed(vm)
            else:
                script_result = self.build_repro_script()
                if isinstance(script_result, Error):
                    self.set_error(script_result)
                    return

                self.state = VmState.extensions_launch
        else:
            nsg = NSG(
                name=vm.region,
                region=vm.region,
            )
            result = nsg.create()
            if isinstance(result, Error):
                self.set_failed(result)
                return

            nsg_config = NetworkSecurityGroupConfig(
                allowed_service_tags=[], allowed_ips=["*"]
            )
            result = nsg.set_allowed_sources(nsg_config)
            if isinstance(result, Error):
                self.set_failed(result)
                return

            vm.nsg = nsg
            result = vm.create()
            if isinstance(result, Error):
                self.set_error(result)
                return
        self.save()

    def set_failed(self, vm_data: VirtualMachine) -> None:
        errors = []
        for status in vm_data.instance_view.statuses:
            if status.level.name.lower() == "error":
                errors.append(
                    "%s %s %s" % (status.code, status.display_status, status.message)
                )
        return self.set_error(Error(code=ErrorCode.VM_CREATE_FAILED, errors=errors))

    def get_setup_container(self) -> Optional[Container]:
        task = Task.get_by_task_id(self.task_id)
        if isinstance(task, Task):
            for container in task.config.containers:
                if container.type == ContainerType.setup:
                    return container.name
        return None

    def extensions_launch(self) -> None:
        vm = self.get_vm()
        vm_data = vm.get()
        if not vm_data:
            self.set_error(
                Error(
                    code=ErrorCode.VM_CREATE_FAILED,
                    errors=["failed before launching extensions"],
                )
            )
            return

        if vm_data.provisioning_state == "Failed":
            self.set_failed(vm_data)
            return

        if not self.ip:
            self.ip = get_public_ip(vm_data.network_profile.network_interfaces[0].id)

        extensions = repro_extensions(
            vm.region, self.os, self.vm_id, self.config, self.get_setup_container()
        )
        result = vm.add_extensions(extensions)
        if isinstance(result, Error):
            self.set_error(result)
            return
        elif result:
            self.state = VmState.running

        self.save()

    def stopping(self) -> None:
        vm = self.get_vm()
        if not vm.is_deleted():
            logging.info("vm stopping: %s", self.vm_id)
            vm.delete()
            self.save()
        else:
            self.stopped()

    def stopped(self) -> None:
        logging.info("vm stopped: %s", self.vm_id)
        self.delete()

    def build_repro_script(self) -> Optional[Error]:
        if self.auth is None:
            return Error(code=ErrorCode.VM_CREATE_FAILED, errors=["missing auth"])

        task = Task.get_by_task_id(self.task_id)
        if isinstance(task, Error):
            return task

        report = get_report(self.config.container, self.config.path)
        if report is None:
            return Error(code=ErrorCode.VM_CREATE_FAILED, errors=["missing report"])

        if report.input_blob is None:
            return Error(
                code=ErrorCode.VM_CREATE_FAILED,
                errors=["unable to perform repro for crash reports without inputs"],
            )

        files = {}

        if task.os == OS.windows:
            ssh_path = "$env:ProgramData/ssh/administrators_authorized_keys"
            cmds = [
                'Set-Content -Path %s -Value "%s"' % (ssh_path, self.auth.public_key),
                ". C:\\onefuzz\\tools\\win64\\onefuzz.ps1",
                "Set-SetSSHACL",
                'while (1) { cdb -server tcp:port=1337 -c "g" setup\\%s %s }'
                % (
                    task.config.task.target_exe,
                    report.input_blob.name,
                ),
            ]
            cmd = "\r\n".join(cmds)
            files["repro.ps1"] = cmd
        elif task.os == OS.linux:
            gdb_fmt = (
                "ASAN_OPTIONS='abort_on_error=1' gdbserver "
                "%s /onefuzz/setup/%s /onefuzz/downloaded/%s"
            )
            cmd = "while :; do %s; done" % (
                gdb_fmt
                % (
                    "localhost:1337",
                    task.config.task.target_exe,
                    report.input_blob.name,
                )
            )
            files["repro.sh"] = cmd

            cmd = "#!/bin/bash\n%s" % (
                gdb_fmt % ("-", task.config.task.target_exe, report.input_blob.name)
            )
            files["repro-stdout.sh"] = cmd
        else:
            raise NotImplementedError("invalid task os: %s" % task.os)

        for filename in files:
            save_blob(
                Container("repro-scripts"),
                "%s/%s" % (self.vm_id, filename),
                files[filename],
                StorageType.config,
            )

        logging.info("saved repro script")
        return None

    @classmethod
    def search_states(cls, *, states: Optional[List[VmState]] = None) -> List["Repro"]:
        query: QueryFilter = {}
        if states:
            query["state"] = states
        return cls.search(query=query)

    @classmethod
    def create(
        cls, config: ReproConfig, user_info: Optional[UserInfo]
    ) -> Union[Error, "Repro"]:
        report = get_report(config.container, config.path)
        if not report:
            return Error(
                code=ErrorCode.UNABLE_TO_FIND, errors=["unable to find report"]
            )

        task = Task.get_by_task_id(report.task_id)
        if isinstance(task, Error):
            return task

        vm = cls(config=config, task_id=task.task_id, os=task.os, auth=build_auth())
        if vm.end_time is None:
            vm.end_time = datetime.utcnow() + timedelta(hours=config.duration)

        vm.user_info = user_info
        vm.save()

        return vm

    @classmethod
    def search_expired(cls) -> List["Repro"]:
        # unlike jobs/tasks, the entry is deleted from the backing table upon stop
        time_filter = "end_time lt datetime'%s'" % datetime.utcnow().isoformat()
        return cls.search(raw_unchecked_filter=time_filter)

    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        return ("vm_id", None)
