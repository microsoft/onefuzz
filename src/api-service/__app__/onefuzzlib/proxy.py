#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import logging
from typing import List, Optional, Tuple

from azure.mgmt.compute.models import VirtualMachine
from onefuzztypes.enums import VmState
from onefuzztypes.models import (
    Authentication,
    Error,
    Forward,
    ProxyConfig,
    ProxyHeartbeat,
)
from onefuzztypes.primitives import Region
from pydantic import Field

from .__version__ import __version__
from .azure.auth import build_auth
from .azure.containers import get_file_sas_url, save_blob
from .azure.ip import get_public_ip
from .azure.queue import get_queue_sas
from .azure.storage import StorageType
from .azure.vm import VM
from .extension import proxy_manager_extensions
from .orm import MappingIntStrAny, ORMMixin, QueryFilter
from .proxy_forward import ProxyForward

PROXY_SKU = "Standard_B2s"
PROXY_IMAGE = "Canonical:UbuntuServer:18.04-LTS:latest"


# This isn't intended to ever be shared to the client, hence not being in
# onefuzztypes
class Proxy(ORMMixin):
    region: Region
    state: VmState = Field(default=VmState.init)
    auth: Authentication = Field(default_factory=build_auth)
    ip: Optional[str]
    error: Optional[str]
    version: str = Field(default=__version__)
    heartbeat: Optional[ProxyHeartbeat]

    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        return ("region", None)

    def event_include(self) -> Optional[MappingIntStrAny]:
        return {
            "region": ...,
            "state": ...,
            "ip": ...,
            "error": ...,
        }

    def get_vm(self) -> VM:
        vm = VM(
            name="proxy-%s" % self.region,
            region=self.region,
            sku=PROXY_SKU,
            image=PROXY_IMAGE,
            auth=self.auth,
        )
        return vm

    def init(self) -> None:
        vm = self.get_vm()
        vm_data = vm.get()
        if vm_data:
            if vm_data.provisioning_state == "Failed":
                self.set_failed(vm)
            else:
                self.save_proxy_config()
                self.state = VmState.extensions_launch
        else:
            result = vm.create()
            if isinstance(result, Error):
                self.error = repr(result)
                self.state = VmState.stopping
        self.save()

    def set_failed(self, vm_data: VirtualMachine) -> None:
        logging.error("vm failed to provision: %s", vm_data.name)
        for status in vm_data.instance_view.statuses:
            if status.level.name.lower() == "error":
                logging.error(
                    "vm status: %s %s %s %s",
                    vm_data.name,
                    status.code,
                    status.display_status,
                    status.message,
                )
        self.state = VmState.vm_allocation_failed

    def extensions_launch(self) -> None:
        vm = self.get_vm()
        vm_data = vm.get()
        if not vm_data:
            logging.error("Azure VM does not exist: %s", vm.name)
            self.state = VmState.stopping
            self.save()
            return

        if vm_data.provisioning_state == "Failed":
            self.set_failed(vm_data)
            self.save()
            return

        ip = get_public_ip(vm_data.network_profile.network_interfaces[0].id)
        if ip is None:
            self.save()
            return
        self.ip = ip

        extensions = proxy_manager_extensions(self.region)
        result = vm.add_extensions(extensions)
        if isinstance(result, Error):
            logging.error("vm extension failed: %s", repr(result))
            self.error = repr(result)
            self.state = VmState.stopping
        elif result:
            self.state = VmState.running

        self.save()

    def stopping(self) -> None:
        vm = self.get_vm()
        if not vm.is_deleted():
            logging.info("stopping proxy: %s", self.region)
            vm.delete()
            self.save()
        else:
            self.stopped()

    def stopped(self) -> None:
        logging.info("removing proxy: %s", self.region)
        self.delete()

    def is_used(self) -> bool:
        if len(self.get_forwards()) == 0:
            logging.info("proxy has no forwards: %s", self.region)
            return False
        return True

    def is_alive(self) -> bool:
        # Unfortunately, with and without TZ information is required for compare
        # or exceptions are generated
        ten_minutes_ago_no_tz = datetime.datetime.utcnow() - datetime.timedelta(
            minutes=10
        )
        ten_minutes_ago = ten_minutes_ago_no_tz.astimezone(datetime.timezone.utc)
        if (
            self.heartbeat is not None
            and self.heartbeat.timestamp < ten_minutes_ago_no_tz
        ):
            logging.error(
                "proxy last heartbeat is more than an 10 minutes old: %s", self.region
            )
            return False

        elif not self.heartbeat and self.Timestamp and self.Timestamp < ten_minutes_ago:
            logging.error(
                "proxy has no heartbeat in the last 10 minutes: %s", self.region
            )
            return False

        return True

    def get_forwards(self) -> List[Forward]:
        forwards: List[Forward] = []
        for entry in ProxyForward.search_forward(region=self.region):
            if entry.endtime < datetime.datetime.now(tz=datetime.timezone.utc):
                entry.delete()
            else:
                forwards.append(
                    Forward(
                        src_port=entry.port,
                        dst_ip=entry.dst_ip,
                        dst_port=entry.dst_port,
                    )
                )
        return forwards

    def save_proxy_config(self) -> None:
        forwards = self.get_forwards()
        proxy_config = ProxyConfig(
            url=get_file_sas_url(
                "proxy-configs",
                "%s/config.json" % self.region,
                StorageType.config,
                read=True,
            ),
            notification=get_queue_sas(
                "proxy",
                StorageType.config,
                add=True,
            ),
            forwards=forwards,
            region=self.region,
        )

        save_blob(
            "proxy-configs",
            "%s/config.json" % self.region,
            proxy_config.json(),
            StorageType.config,
        )

    @classmethod
    def search_states(cls, *, states: Optional[List[VmState]] = None) -> List["Proxy"]:
        query: QueryFilter = {}
        if states:
            query["state"] = states
        return cls.search(query=query)

    @classmethod
    def get_or_create(cls, region: Region) -> Optional["Proxy"]:
        proxy = Proxy.get(region)
        if proxy is not None:
            if proxy.version != __version__:
                if proxy.state != VmState.stopping:
                    # If the proxy is out-of-date, delete and re-create it
                    proxy.state = VmState.stopping
                    proxy.save()
                return None
            return proxy

        proxy = Proxy(region=region)
        proxy.save()
        return proxy
