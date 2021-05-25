#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import logging
from typing import List, Optional, Tuple, Union
from uuid import UUID

from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error, Forward
from onefuzztypes.primitives import Region
from pydantic import Field

from .azure.ip import get_scaleset_instance_ip
from .orm import ORMMixin, QueryFilter

PORT_RANGES = range(28000, 32000)


# This isn't intended to ever be shared to the client, hence not being in
# onefuzztypes
class ProxyForward(ORMMixin):
    region: Region
    port: int
    scaleset_id: UUID
    machine_id: UUID
    proxy_id: Optional[UUID]
    dst_ip: str
    dst_port: int
    endtime: datetime.datetime = Field(default_factory=datetime.datetime.utcnow)

    @classmethod
    def key_fields(cls) -> Tuple[str, str]:
        return ("region", "port")

    @classmethod
    def update_or_create(
        cls,
        region: Region,
        scaleset_id: UUID,
        machine_id: UUID,
        dst_port: int,
        duration: int,
    ) -> Union["ProxyForward", Error]:
        private_ip = get_scaleset_instance_ip(scaleset_id, machine_id)
        if not private_ip:
            return Error(
                code=ErrorCode.UNABLE_TO_PORT_FORWARD, errors=["no private ip for node"]
            )

        entries = cls.search_forward(
            scaleset_id=scaleset_id,
            machine_id=machine_id,
            dst_port=dst_port,
            region=region,
        )
        if entries:
            entry = entries[0]
            entry.endtime = datetime.datetime.utcnow() + datetime.timedelta(
                hours=duration
            )
            entry.save()
            return entry

        existing = [int(x.port) for x in entries]
        for port in PORT_RANGES:
            if port in existing:
                continue

            entry = cls(
                region=region,
                port=port,
                scaleset_id=scaleset_id,
                machine_id=machine_id,
                dst_ip=private_ip,
                dst_port=dst_port,
                endtime=datetime.datetime.utcnow() + datetime.timedelta(hours=duration),
            )
            result = entry.save(new=True)
            if isinstance(result, Error):
                logging.info("port is already used: %s", entry)
                continue

            return entry

        return Error(
            code=ErrorCode.UNABLE_TO_PORT_FORWARD, errors=["all forward ports used"]
        )

    @classmethod
    def remove_forward(
        cls,
        scaleset_id: UUID,
        *,
        proxy_id: Optional[UUID] = None,
        machine_id: Optional[UUID] = None,
        dst_port: Optional[int] = None,
    ) -> List[Region]:
        entries = cls.search_forward(
            scaleset_id=scaleset_id,
            machine_id=machine_id,
            proxy_id=proxy_id,
            dst_port=dst_port,
        )
        regions = set()
        for entry in entries:
            regions.add(entry.region)
            entry.delete()
        return list(regions)

    @classmethod
    def search_forward(
        cls,
        *,
        scaleset_id: Optional[UUID] = None,
        region: Optional[Region] = None,
        machine_id: Optional[UUID] = None,
        proxy_id: Optional[UUID] = None,
        dst_port: Optional[int] = None,
    ) -> List["ProxyForward"]:

        query: QueryFilter = {}
        if region is not None:
            query["region"] = [region]

        if scaleset_id is not None:
            query["scaleset_id"] = [scaleset_id]

        if machine_id is not None:
            query["machine_id"] = [machine_id]

        if proxy_id is not None:
            query["proxy_id"] = [proxy_id]

        if dst_port is not None:
            query["dst_port"] = [dst_port]

        return cls.search(query=query)

    def to_forward(self) -> Forward:
        return Forward(src_port=self.port, dst_ip=self.dst_ip, dst_port=self.dst_port)
