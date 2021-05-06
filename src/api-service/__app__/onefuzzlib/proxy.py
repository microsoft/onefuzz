#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import logging
import os
from typing import List, Optional, Tuple
from uuid import UUID, uuid4

from azure.mgmt.compute.models import VirtualMachine
from onefuzztypes.enums import ErrorCode, VmState
from onefuzztypes.events import EventProxyCreated, EventProxyDeleted, EventProxyFailed
from onefuzztypes.models import (
    Authentication,
    Error,
    Forward,
    ProxyConfig,
    ProxyHeartbeat,
)
from onefuzztypes.primitives import Container, Region
from pydantic import Field

from .__version__ import __version__
from .azure.auth import build_auth
from .azure.containers import get_file_sas_url, save_blob
from .azure.creds import get_instance_id
from .azure.ip import get_public_ip
from .azure.queue import get_queue_sas
from .azure.storage import StorageType
from .azure.vm import VM
from .events import send_event
from .extension import proxy_manager_extensions
from .orm import ORMMixin, QueryFilter
from .proxy_forward import ProxyForward

PROXY_SKU = "Standard_B2s"
PROXY_IMAGE = "Canonical:UbuntuServer:18.04-LTS:latest"
PROXY_LOG_PREFIX = "scaleset-proxy: "


# This isn't intended to ever be shared to the client, hence not being in
# onefuzztypes
class Proxy(ORMMixin):
    timestamp: Optional[datetime.datetime] = Field(alias="Timestamp")
    created_timestamp: datetime.datetime = Field(
        default_factory=datetime.datetime.utcnow
    )
    proxy_id: UUID = Field(default_factory=uuid4)
    region: Region
    state: VmState = Field(default=VmState.init)
    auth: Authentication = Field(default_factory=build_auth)
    ip: Optional[str]
    error: Optional[Error]
    version: str = Field(default=__version__)
    heartbeat: Optional[ProxyHeartbeat]
    outdated: bool = Field(default=False)

    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        return ("region", "proxy_id")

    def get_vm(self) -> VM:
        vm = VM(
            name="proxy-%s-%s" % (self.region, self.proxy_id),
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
                self.set_provision_failed(vm_data)
                return
            else:
                self.save_proxy_config()
                self.state = VmState.extensions_launch
        else:
            result = vm.create()
            if isinstance(result, Error):
                self.set_failed(result)
                return
        self.save()

    def set_provision_failed(self, vm_data: VirtualMachine) -> None:
        errors = ["provisioning failed"]
        for status in vm_data.instance_view.statuses:
            if status.level.name.lower() == "error":
                errors.append(
                    f"code:{status.code} status:{status.display_status} "
                    f"message:{status.message}"
                )

        self.set_failed(
            Error(
                code=ErrorCode.PROXY_FAILED,
                errors=errors,
            )
        )
        return

    def set_failed(self, error: Error) -> None:
        if self.error is not None:
            return

        logging.error(PROXY_LOG_PREFIX + "vm failed: %s - %s", self.region, error)
        send_event(EventProxyFailed(region=self.region, error=error))
        self.error = error
        self.state = VmState.stopping
        self.save()

    def extensions_launch(self) -> None:
        vm = self.get_vm()
        vm_data = vm.get()
        if not vm_data:
            self.set_failed(
                Error(
                    code=ErrorCode.PROXY_FAILED,
                    errors=["azure not able to find vm"],
                )
            )
            return

        if vm_data.provisioning_state == "Failed":
            self.set_provision_failed(vm_data)
            return

        ip = get_public_ip(vm_data.network_profile.network_interfaces[0].id)
        if ip is None:
            self.save()
            return
        self.ip = ip

        extensions = proxy_manager_extensions(self.region)
        result = vm.add_extensions(extensions)
        if isinstance(result, Error):
            self.set_failed(result)
            return
        elif result:
            self.state = VmState.running

        self.save()

    def stopping(self) -> None:
        vm = self.get_vm()
        if not vm.is_deleted():
            logging.info(PROXY_LOG_PREFIX + "stopping proxy: %s", self.region)
            vm.delete()
            self.save()
        else:
            self.stopped()

    def stopped(self) -> None:
        logging.info(PROXY_LOG_PREFIX + "removing proxy: %s", self.region)
        self.delete()

    def is_outdated(self) -> bool:
        if self.version != __version__:
            logging.info(
                PROXY_LOG_PREFIX + "mismatch version: proxy:%s service:%s state:%s",
                self.version,
                __version__,
                self.state,
            )
            self.outdated = True
            return True
        if self.created_timestamp is not None:
            proxy_timestamp = self.created_timestamp
            if proxy_timestamp < (
                datetime.datetime.now(tz=datetime.timezone.utc)
                - datetime.timedelta(minutes=30)
            ):
                logging.info(
                    PROXY_LOG_PREFIX
                    + "proxy older than 7 days: proxy-created:%s state:%s",
                    self.created_timestamp,
                    self.state,
                )
                self.outdated = True
                return True
        return False

    def is_used(self) -> bool:
        if len(self.get_forwards()) == 0:
            logging.info(PROXY_LOG_PREFIX + "no forwards: %s", self.region)
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
                PROXY_LOG_PREFIX + "last heartbeat is more than an 10 minutes old: "
                "%s - last heartbeat:%s compared_to:%s",
                self.region,
                self.heartbeat,
                ten_minutes_ago_no_tz,
            )
            return False

        elif not self.heartbeat and self.timestamp and self.timestamp < ten_minutes_ago:
            logging.error(
                PROXY_LOG_PREFIX + "no heartbeat in the last 10 minutes: "
                "%s timestamp: %s compared_to:%s",
                self.region,
                self.timestamp,
                ten_minutes_ago,
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
                Container("proxy-configs"),
                "%s/%s/config.json" % (self.region, self.proxy_id),
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
            instance_telemetry_key=os.environ.get("APPINSIGHTS_INSTRUMENTATIONKEY"),
            microsoft_telemetry_key=os.environ.get("ONEFUZZ_TELEMETRY"),
            instance_id=get_instance_id(),
        )

        save_blob(
            Container("proxy-configs"),
            "%s/%s/config.json" % (self.region, self.proxy_id),
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
    # Question - Why does this not include is_used to check forwards?
    def get_or_create(cls, region: Region) -> Optional["Proxy"]:
        proxy = Proxy.search(
            query={"region": [region], "outdated": [False]}, num_results=1
        )[0]
        proxy_timestamp = None
        if proxy is not None:
            if proxy.version != __version__:
                logging.info(
                    PROXY_LOG_PREFIX
                    + "mismatch  version: proxy:%s service:%s state:%s",
                    proxy.version,
                    __version__,
                    proxy.state,
                )
                if proxy.state != VmState.stopping:
                    # If the proxy is out-of-date, delete and re-create it
                    proxy.state = VmState.stopping
                    proxy.save()
                proxy.outdated = True
                return None
            if proxy.created_timestamp is not None:
                proxy_timestamp = proxy.created_timestamp
                if proxy_timestamp < (
                    datetime.datetime.now(tz=datetime.timezone.utc)
                    - datetime.timedelta(minutes=30)
                ):
                    logging.info(
                        PROXY_LOG_PREFIX
                        + "proxy older than 7 days: proxy-created:%s state:%s",
                        proxy.created_timestamp,
                        proxy.state,
                    )
                    if proxy.state != VmState.stopping:
                        # If the proxy is out-of-date, delete and re-create it
                        proxy.state = VmState.stopping
                        proxy.save()
                    proxy.outdated = True
                    return None
            return proxy

        logging.info(PROXY_LOG_PREFIX + "creating proxy: region:%s", region)
        proxy = Proxy(region=region)
        proxy.save()
        send_event(EventProxyCreated(region=region))
        return proxy

    def delete(self) -> None:
        super().delete()
        send_event(EventProxyDeleted(region=self.region))
