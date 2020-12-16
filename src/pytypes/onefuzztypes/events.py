#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from datetime import datetime
from enum import Enum
from typing import Optional, Union
from uuid import UUID, uuid4

from pydantic import BaseModel, Extra, Field

from .enums import OS, Architecture, NodeState, TaskState
from .models import AutoScaleConfig, Error, JobConfig, Report, TaskConfig, UserInfo
from .primitives import Container, Region
from .responses import BaseResponse


class BaseEvent(BaseModel):
    class Config:
        extra = Extra.forbid


class EventTaskStopped(BaseEvent):
    job_id: UUID
    task_id: UUID
    user_info: Optional[UserInfo]


class EventTaskFailed(BaseEvent):
    job_id: UUID
    task_id: UUID
    error: Error
    user_info: Optional[UserInfo]


class EventJobCreated(BaseEvent):
    job_id: UUID
    config: JobConfig
    user_info: Optional[UserInfo]


class EventJobStopped(BaseEvent):
    job_id: UUID
    config: JobConfig
    user_info: Optional[UserInfo]


class EventTaskCreated(BaseEvent):
    job_id: UUID
    task_id: UUID
    config: TaskConfig
    user_info: Optional[UserInfo]


class EventTaskStateUpdated(BaseEvent):
    job_id: UUID
    task_id: UUID
    state: TaskState
    end_time: Optional[datetime]


class EventPing(BaseResponse):
    ping_id: UUID


class EventScalesetCreated(BaseEvent):
    scaleset_id: UUID
    pool_name: str
    vm_sku: str
    image: str
    region: Region
    size: int


class EventScalesetFailed(BaseEvent):
    scaleset_id: UUID
    pool_name: str
    error: Error


class EventScalesetDeleted(BaseEvent):
    scaleset_id: UUID
    pool_name: str


class EventPoolDeleted(BaseEvent):
    pool_name: str


class EventPoolCreated(BaseEvent):
    pool_name: str
    os: OS
    arch: Architecture
    managed: bool
    autoscale: Optional[AutoScaleConfig]


class EventProxyCreated(BaseEvent):
    region: Region


class EventProxyDeleted(BaseEvent):
    region: Region


class EventProxyFailed(BaseEvent):
    region: Region
    error: Error


class EventNodeCreated(BaseEvent):
    machine_id: UUID
    scaleset_id: Optional[UUID]
    pool_name: str


class EventNodeDeleted(BaseEvent):
    machine_id: UUID
    scaleset_id: Optional[UUID]
    pool_name: str


class EventNodeStateUpdated(BaseEvent):
    machine_id: UUID
    scaleset_id: Optional[UUID]
    pool_name: str
    state: NodeState


class EventCrashReported(BaseEvent):
    report: Report
    container: Container
    filename: str


class EventFileAdded(BaseEvent):
    container: Container
    filename: str


Event = Union[
    EventJobCreated,
    EventJobStopped,
    EventNodeStateUpdated,
    EventNodeCreated,
    EventNodeDeleted,
    EventPing,
    EventPoolCreated,
    EventPoolDeleted,
    EventProxyFailed,
    EventProxyCreated,
    EventProxyDeleted,
    EventScalesetFailed,
    EventScalesetCreated,
    EventScalesetDeleted,
    EventTaskFailed,
    EventTaskStateUpdated,
    EventTaskCreated,
    EventTaskStopped,
    EventCrashReported,
    EventFileAdded,
]


class EventType(Enum):
    job_created = "job_created"
    job_stopped = "job_stopped"
    node_created = "node_created"
    node_deleted = "node_deleted"
    node_state_updated = "node_state_updated"
    ping = "ping"
    pool_created = "pool_created"
    pool_deleted = "pool_deleted"
    proxy_created = "proxy_created"
    proxy_deleted = "proxy_deleted"
    proxy_failed = "proxy_failed"
    scaleset_created = "scaleset_created"
    scaleset_deleted = "scaleset_deleted"
    scaleset_failed = "scaleset_failed"
    task_created = "task_created"
    task_failed = "task_failed"
    task_state_updated = "task_state_updated"
    task_stopped = "task_stopped"
    crash_reported = "crash_reported"
    file_added = "file_added"


EventTypeMap = {
    EventType.job_created: EventJobCreated,
    EventType.job_stopped: EventJobStopped,
    EventType.node_created: EventNodeCreated,
    EventType.node_deleted: EventNodeDeleted,
    EventType.node_state_updated: EventNodeStateUpdated,
    EventType.ping: EventPing,
    EventType.pool_created: EventPoolCreated,
    EventType.pool_deleted: EventPoolDeleted,
    EventType.proxy_created: EventProxyCreated,
    EventType.proxy_deleted: EventProxyDeleted,
    EventType.proxy_failed: EventProxyFailed,
    EventType.scaleset_created: EventScalesetCreated,
    EventType.scaleset_deleted: EventScalesetDeleted,
    EventType.scaleset_failed: EventScalesetFailed,
    EventType.task_created: EventTaskCreated,
    EventType.task_failed: EventTaskFailed,
    EventType.task_state_updated: EventTaskStateUpdated,
    EventType.task_stopped: EventTaskStopped,
    EventType.crash_reported: EventCrashReported,
    EventType.file_added: EventFileAdded,
}


def get_event_type(event: Event) -> EventType:

    for (event_type, event_class) in EventTypeMap.items():
        if isinstance(event, event_class):
            return event_type

    raise NotImplementedError("unsupported event type: %s" % type(event))


class EventMessage(BaseEvent):
    event_id: UUID = Field(default_factory=uuid4)
    event_type: EventType
    event: Event
