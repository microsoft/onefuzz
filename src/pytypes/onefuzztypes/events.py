#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from enum import Enum
from typing import Optional, Union
from uuid import UUID, uuid4

from pydantic import BaseModel, Field

from .enums import OS, Architecture
from .models import AutoScaleConfig, Error, JobConfig, TaskConfig, UserInfo
from .primitives import Region
from .responses import BaseResponse


class EventTaskStopped(BaseModel):
    job_id: UUID
    task_id: UUID
    user_info: Optional[UserInfo]


class EventTaskFailed(BaseModel):
    job_id: UUID
    task_id: UUID
    error: Error
    user_info: Optional[UserInfo]


class EventJobCreated(BaseModel):
    job_id: UUID
    config: JobConfig
    user_info: Optional[UserInfo]


class EventTaskCreated(BaseModel):
    job_id: UUID
    task_id: UUID
    config: TaskConfig
    user_info: Optional[UserInfo]


class EventPing(BaseResponse):
    ping_id: UUID = Field(default_factory=uuid4)


class EventScalesetCreated(BaseModel):
    scaleset_id: UUID
    pool_name: str
    vm_sku: str
    image: str
    region: Region
    size: int


class EventScalesetFailed(BaseModel):
    scaleset_id: UUID
    pool_name: str
    error: Error


class EventScalesetDeleted(BaseModel):
    scaleset_id: UUID
    pool_name: str


class EventPoolDeleted(BaseModel):
    pool_name: str


class EventPoolCreated(BaseModel):
    pool_name: str
    os: OS
    arch: Architecture
    managed: bool
    autoscale: Optional[AutoScaleConfig]


class EventProxyCreated(BaseModel):
    region: Region


class EventProxyDeleted(BaseModel):
    region: Region


class EventProxyFailed(BaseModel):
    region: Region
    error: Error


Event = Union[
    EventProxyCreated,
    EventProxyDeleted,
    EventProxyFailed,
    EventPoolCreated,
    EventPoolDeleted,
    EventScalesetCreated,
    EventScalesetFailed,
    EventScalesetDeleted,
    EventTaskCreated,
    EventTaskStopped,
    EventTaskFailed,
    EventJobCreated,
    EventPing,
]


class EventType(Enum):
    task_created = "task_created"
    task_stopped = "task_stopped"
    task_failed = "task_failed"
    ping = "ping"
    job_created = "job_created"
    pool_created = "pool_created"
    pool_deleted = "pool_deleted"
    proxy_created = "proxy_created"
    proxy_deleted = "proxy_deleted"
    proxy_failed = "proxy_failed"
    scaleset_created = "scaleset_created"
    scaleset_deleted = "scaleset_deleted"
    scaleset_failed = "scaleset_failed"


def get_event_type(event: Event) -> EventType:
    events = {
        EventTaskCreated: EventType.task_created,
        EventTaskFailed: EventType.task_failed,
        EventTaskStopped: EventType.task_stopped,
        EventPing: EventType.ping,
        EventPoolCreated: EventType.pool_created,
        EventPoolDeleted: EventType.pool_deleted,
        EventJobCreated: EventType.job_created,
        EventProxyCreated: EventType.proxy_created,
        EventProxyDeleted: EventType.proxy_deleted,
        EventProxyFailed: EventType.proxy_failed,
        EventScalesetCreated: EventType.scaleset_created,
        EventScalesetDeleted: EventType.scaleset_deleted,
        EventScalesetFailed: EventType.scaleset_failed,
    }

    for event_class in events:
        if isinstance(event, event_class):
            return events[event_class]

    raise NotImplementedError("unsupported event type: %s" % type(event))
