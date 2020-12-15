#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from enum import Enum
from typing import Optional, Union
from uuid import UUID, uuid4

from pydantic import BaseModel, Field

from .enums import OS, Architecture, NodeState, TaskState
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


class EventJobStopped(BaseModel):
    job_id: UUID
    config: JobConfig
    user_info: Optional[UserInfo]


class EventTaskCreated(BaseModel):
    job_id: UUID
    task_id: UUID
    config: TaskConfig
    user_info: Optional[UserInfo]


class EventTaskStateUpdated(BaseModel):
    job_id: UUID
    task_id: UUID
    state: TaskState


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


class EventNodeCreated(BaseModel):
    machine_id: UUID
    scaleset_id: Optional[UUID]
    pool_name: str


class EventNodeDeleted(BaseModel):
    machine_id: UUID
    scaleset_id: Optional[UUID]
    pool_name: str


class EventNodeStateUpdated(BaseModel):
    machine_id: UUID
    scaleset_id: Optional[UUID]
    pool_name: str
    state: NodeState


Event = Union[
    EventJobCreated,
    EventJobStopped,
    EventNodeCreated,
    EventNodeDeleted,
    EventNodeStateUpdated,
    EventPing,
    EventPoolCreated,
    EventPoolDeleted,
    EventProxyCreated,
    EventProxyDeleted,
    EventProxyFailed,
    EventScalesetCreated,
    EventScalesetDeleted,
    EventScalesetFailed,
    EventTaskCreated,
    EventTaskFailed,
    EventTaskStateUpdated,
    EventTaskStopped,
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


def get_event_type(event: Event) -> EventType:
    events = {
        EventJobCreated: EventType.job_created,
        EventJobStopped: EventType.job_stopped,
        EventNodeCreated: EventType.node_created,
        EventNodeDeleted: EventType.node_deleted,
        EventNodeStateUpdated: EventType.node_state_updated,
        EventPing: EventType.ping,
        EventPoolCreated: EventType.pool_created,
        EventPoolDeleted: EventType.pool_deleted,
        EventProxyCreated: EventType.proxy_created,
        EventProxyDeleted: EventType.proxy_deleted,
        EventProxyFailed: EventType.proxy_failed,
        EventScalesetCreated: EventType.scaleset_created,
        EventScalesetDeleted: EventType.scaleset_deleted,
        EventScalesetFailed: EventType.scaleset_failed,
        EventTaskCreated: EventType.task_created,
        EventTaskFailed: EventType.task_failed,
        EventTaskStateUpdated: EventType.task_state_updated,
        EventTaskStopped: EventType.task_stopped,
    }

    for event_class in events:
        if isinstance(event, event_class):
            return events[event_class]

    raise NotImplementedError("unsupported event type: %s" % type(event))


class EventMessage(BaseModel):
    event_id: UUID = Field(default_factory=uuid4)
    event_type: EventType
    event: Event
