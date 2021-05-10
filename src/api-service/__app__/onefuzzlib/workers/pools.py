#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import logging
from typing import List, Optional, Tuple, Union
from uuid import UUID

from onefuzztypes.enums import OS, Architecture, ErrorCode, PoolState, ScalesetState
from onefuzztypes.events import EventPoolCreated, EventPoolDeleted
from onefuzztypes.models import AutoScaleConfig, Error
from onefuzztypes.models import Pool as BASE_POOL
from onefuzztypes.models import (
    ScalesetSummary,
    WorkSet,
    WorkSetSummary,
    WorkUnitSummary,
)
from onefuzztypes.primitives import PoolName

from ..azure.queue import create_queue, delete_queue, peek_queue, queue_object
from ..azure.storage import StorageType
from ..events import send_event
from ..orm import MappingIntStrAny, ORMMixin, QueryFilter

NODE_EXPIRATION_TIME: datetime.timedelta = datetime.timedelta(hours=1)
NODE_REIMAGE_TIME: datetime.timedelta = datetime.timedelta(days=7)

# Future work:
#
# Enabling autoscaling for the scalesets based on the pool work queues.
# https://docs.microsoft.com/en-us/azure/azure-monitor/platform/autoscale-common-metrics#commonly-used-storage-metrics


class Pool(BASE_POOL, ORMMixin):
    @classmethod
    def create(
        cls,
        *,
        name: PoolName,
        os: OS,
        arch: Architecture,
        managed: bool,
        client_id: Optional[UUID],
        autoscale: Optional[AutoScaleConfig],
    ) -> "Pool":
        entry = cls(
            name=name,
            os=os,
            arch=arch,
            managed=managed,
            client_id=client_id,
            config=None,
            autoscale=autoscale,
        )
        entry.save()
        send_event(
            EventPoolCreated(
                pool_name=name,
                os=os,
                arch=arch,
                managed=managed,
                autoscale=autoscale,
            )
        )
        return entry

    def save_exclude(self) -> Optional[MappingIntStrAny]:
        return {
            "nodes": ...,
            "queue": ...,
            "work_queue": ...,
            "config": ...,
            "node_summary": ...,
        }

    def export_exclude(self) -> Optional[MappingIntStrAny]:
        return {
            "etag": ...,
            "timestamp": ...,
        }

    def telemetry_include(self) -> Optional[MappingIntStrAny]:
        return {
            "pool_id": ...,
            "os": ...,
            "state": ...,
            "managed": ...,
        }

    def populate_scaleset_summary(self) -> None:
        from .scalesets import Scaleset

        self.scaleset_summary = [
            ScalesetSummary(scaleset_id=x.scaleset_id, state=x.state)
            for x in Scaleset.search_by_pool(self.name)
        ]

    def populate_work_queue(self) -> None:
        self.work_queue = []

        # Only populate the work queue summaries if the pool is initialized. We
        # can then be sure that the queue is available in the operations below.
        if self.state == PoolState.init:
            return

        worksets = peek_queue(
            self.get_pool_queue(), StorageType.corpus, object_type=WorkSet
        )

        for workset in worksets:
            work_units = [
                WorkUnitSummary(
                    job_id=work_unit.job_id,
                    task_id=work_unit.task_id,
                    task_type=work_unit.task_type,
                )
                for work_unit in workset.work_units
            ]
            self.work_queue.append(WorkSetSummary(work_units=work_units))

    def get_pool_queue(self) -> str:
        return "pool-%s" % self.pool_id.hex

    def init(self) -> None:
        create_queue(self.get_pool_queue(), StorageType.corpus)
        self.state = PoolState.running
        self.save()

    def schedule_workset(self, work_set: WorkSet) -> bool:
        # Don't schedule work for pools that can't and won't do work.
        if self.state in [PoolState.shutdown, PoolState.halt]:
            return False

        return queue_object(
            self.get_pool_queue(),
            work_set,
            StorageType.corpus,
        )

    @classmethod
    def get_by_id(cls, pool_id: UUID) -> Union[Error, "Pool"]:
        pools = cls.search(query={"pool_id": [pool_id]})
        if not pools:
            return Error(code=ErrorCode.INVALID_REQUEST, errors=["unable to find pool"])

        if len(pools) != 1:
            return Error(
                code=ErrorCode.INVALID_REQUEST, errors=["error identifying pool"]
            )
        pool = pools[0]
        return pool

    @classmethod
    def get_by_name(cls, name: PoolName) -> Union[Error, "Pool"]:
        pools = cls.search(query={"name": [name]})
        if not pools:
            return Error(code=ErrorCode.INVALID_REQUEST, errors=["unable to find pool"])

        if len(pools) != 1:
            return Error(
                code=ErrorCode.INVALID_REQUEST, errors=["error identifying pool"]
            )
        pool = pools[0]
        return pool

    @classmethod
    def search_states(cls, *, states: Optional[List[PoolState]] = None) -> List["Pool"]:
        query: QueryFilter = {}
        if states:
            query["state"] = states
        return cls.search(query=query)

    def set_shutdown(self, now: bool) -> None:
        if self.state in [PoolState.halt, PoolState.shutdown]:
            return

        if now:
            self.state = PoolState.halt
        else:
            self.state = PoolState.shutdown

        self.save()

    def shutdown(self) -> None:
        """shutdown allows nodes to finish current work then delete"""
        from .nodes import Node
        from .scalesets import Scaleset

        scalesets = Scaleset.search_by_pool(self.name)
        nodes = Node.search(query={"pool_name": [self.name]})
        if not scalesets and not nodes:
            logging.info("pool stopped, deleting: %s", self.name)

            self.state = PoolState.halt
            self.delete()
            return

        for scaleset in scalesets:
            scaleset.set_shutdown(now=False)

        for node in nodes:
            node.set_shutdown()

        self.save()

    def halt(self) -> None:
        """halt the pool immediately"""

        from .nodes import Node
        from .scalesets import Scaleset

        scalesets = Scaleset.search_by_pool(self.name)
        nodes = Node.search(query={"pool_name": [self.name]})
        if not scalesets and not nodes:
            delete_queue(self.get_pool_queue(), StorageType.corpus)
            logging.info("pool stopped, deleting: %s", self.name)
            self.state = PoolState.halt
            self.delete()
            return

        for scaleset in scalesets:
            scaleset.state = ScalesetState.halt
            scaleset.save()

        for node in nodes:
            node.set_halt()

        self.save()

    @classmethod
    def key_fields(cls) -> Tuple[str, str]:
        return ("name", "pool_id")

    def delete(self) -> None:
        super().delete()
        send_event(EventPoolDeleted(pool_name=self.name))
