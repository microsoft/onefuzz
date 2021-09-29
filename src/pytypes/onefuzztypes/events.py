#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from datetime import datetime
from enum import Enum
from typing import Any, Dict, List, Optional, Union
from uuid import UUID, uuid4

from pydantic import BaseModel, Field

from ._monkeypatch import _check_hotfix
from .enums import (
    OS,
    Architecture,
    NodeState,
    ScalesetState,
    TaskState,
    TaskType,
    VmState,
)
from .models import (
    AutoScaleConfig,
    Error,
    InstanceConfig,
    JobConfig,
    RegressionReport,
    Report,
    TaskConfig,
    UserInfo,
)
from .primitives import Container, PoolName, Region
from .responses import BaseResponse


class BaseEvent(BaseModel):
    pass


class EventTaskStopped(BaseEvent):
    job_id: UUID
    task_id: UUID
    user_info: Optional[UserInfo]
    config: TaskConfig


class EventTaskFailed(BaseEvent):
    job_id: UUID
    task_id: UUID
    error: Error
    user_info: Optional[UserInfo]
    config: TaskConfig


class EventJobCreated(BaseEvent):
    job_id: UUID
    config: JobConfig
    user_info: Optional[UserInfo]


class JobTaskStopped(BaseModel):
    task_id: UUID
    task_type: TaskType
    error: Optional[Error]


class EventJobStopped(BaseEvent):
    job_id: UUID
    config: JobConfig
    user_info: Optional[UserInfo]
    task_info: Optional[List[JobTaskStopped]]


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
    config: TaskConfig


class EventTaskHeartbeat(BaseEvent):
    job_id: UUID
    task_id: UUID
    config: TaskConfig


class EventPing(BaseEvent, BaseResponse):
    ping_id: UUID


class EventScalesetCreated(BaseEvent):
    scaleset_id: UUID
    pool_name: PoolName
    vm_sku: str
    image: str
    region: Region
    size: int


class EventScalesetFailed(BaseEvent):
    scaleset_id: UUID
    pool_name: PoolName
    error: Error


class EventScalesetDeleted(BaseEvent):
    scaleset_id: UUID
    pool_name: PoolName


class EventScalesetResizeScheduled(BaseEvent):
    scaleset_id: UUID
    pool_name: PoolName
    size: int


class EventPoolDeleted(BaseEvent):
    pool_name: PoolName


class EventPoolCreated(BaseEvent):
    pool_name: PoolName
    os: OS
    arch: Architecture
    managed: bool
    autoscale: Optional[AutoScaleConfig]


class EventProxyCreated(BaseEvent):
    region: Region
    proxy_id: Optional[UUID]


class EventProxyDeleted(BaseEvent):
    region: Region
    proxy_id: Optional[UUID]


class EventProxyFailed(BaseEvent):
    region: Region
    proxy_id: Optional[UUID]
    error: Error


class EventProxyStateUpdated(BaseEvent):
    region: Region
    proxy_id: UUID
    state: VmState


class EventNodeCreated(BaseEvent):
    machine_id: UUID
    scaleset_id: Optional[UUID]
    pool_name: PoolName


class EventNodeHeartbeat(BaseEvent):
    machine_id: UUID
    scaleset_id: Optional[UUID]
    pool_name: PoolName


class EventNodeDeleted(BaseEvent):
    machine_id: UUID
    scaleset_id: Optional[UUID]
    pool_name: PoolName


class EventScalesetStateUpdated(BaseEvent):
    scaleset_id: UUID
    pool_name: PoolName
    state: ScalesetState


class EventNodeStateUpdated(BaseEvent):
    machine_id: UUID
    scaleset_id: Optional[UUID]
    pool_name: PoolName
    state: NodeState


class EventCrashReported(BaseEvent):
    report: Report
    container: Container
    filename: str
    task_config: Optional[TaskConfig]


class EventRegressionReported(BaseEvent):
    regression_report: RegressionReport
    container: Container
    filename: str
    task_config: Optional[TaskConfig]


class EventFileAdded(BaseEvent):
    container: Container
    filename: str


class EventInstanceConfigUpdated(BaseEvent):
    config: InstanceConfig


Event = Union[
    EventJobCreated,
    EventJobStopped,
    EventNodeStateUpdated,
    EventNodeCreated,
    EventNodeDeleted,
    EventNodeHeartbeat,
    EventPing,
    EventPoolCreated,
    EventPoolDeleted,
    EventProxyFailed,
    EventProxyCreated,
    EventProxyDeleted,
    EventProxyStateUpdated,
    EventScalesetFailed,
    EventScalesetCreated,
    EventScalesetDeleted,
    EventScalesetStateUpdated,
    EventScalesetResizeScheduled,
    EventTaskFailed,
    EventTaskStateUpdated,
    EventTaskCreated,
    EventTaskStopped,
    EventTaskHeartbeat,
    EventCrashReported,
    EventRegressionReported,
    EventFileAdded,
    EventInstanceConfigUpdated,
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
    proxy_state_updated = "proxy_state_updated"
    scaleset_created = "scaleset_created"
    scaleset_deleted = "scaleset_deleted"
    scaleset_failed = "scaleset_failed"
    scaleset_state_updated = "scaleset_state_updated"
    scaleset_resize_scheduled = "scaleset_resize_scheduled"
    task_created = "task_created"
    task_failed = "task_failed"
    task_state_updated = "task_state_updated"
    task_stopped = "task_stopped"
    crash_reported = "crash_reported"
    regression_reported = "regression_reported"
    file_added = "file_added"
    task_heartbeat = "task_heartbeat"
    node_heartbeat = "node_heartbeat"
    instance_config_updated = "instance_config_updated"


EventTypeMap = {
    EventType.job_created: EventJobCreated,
    EventType.job_stopped: EventJobStopped,
    EventType.node_created: EventNodeCreated,
    EventType.node_deleted: EventNodeDeleted,
    EventType.node_state_updated: EventNodeStateUpdated,
    EventType.node_heartbeat: EventNodeHeartbeat,
    EventType.ping: EventPing,
    EventType.pool_created: EventPoolCreated,
    EventType.pool_deleted: EventPoolDeleted,
    EventType.proxy_created: EventProxyCreated,
    EventType.proxy_deleted: EventProxyDeleted,
    EventType.proxy_failed: EventProxyFailed,
    EventType.proxy_state_updated: EventProxyStateUpdated,
    EventType.scaleset_created: EventScalesetCreated,
    EventType.scaleset_deleted: EventScalesetDeleted,
    EventType.scaleset_failed: EventScalesetFailed,
    EventType.scaleset_state_updated: EventScalesetStateUpdated,
    EventType.scaleset_resize_scheduled: EventScalesetResizeScheduled,
    EventType.task_created: EventTaskCreated,
    EventType.task_failed: EventTaskFailed,
    EventType.task_state_updated: EventTaskStateUpdated,
    EventType.task_heartbeat: EventTaskHeartbeat,
    EventType.task_stopped: EventTaskStopped,
    EventType.crash_reported: EventCrashReported,
    EventType.regression_reported: EventRegressionReported,
    EventType.file_added: EventFileAdded,
    EventType.instance_config_updated: EventInstanceConfigUpdated,
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
    instance_id: UUID
    instance_name: str


# because Pydantic does not yet have discriminated union types yet, parse events
# by hand.  https://github.com/samuelcolvin/pydantic/issues/619
def parse_event_message(data: Dict[str, Any]) -> EventMessage:
    instance_id = UUID(data["instance_id"])
    instance_name = data["instance_name"]
    event_id = UUID(data["event_id"])
    event_type = EventType[data["event_type"]]
    # mypy incorrectly identifies this as having not supported parse_obj yet
    event = EventTypeMap[event_type].parse_obj(data["event"])  # type: ignore

    return EventMessage(
        event_id=event_id,
        event_type=event_type,
        event=event,
        instance_id=instance_id,
        instance_name=instance_name,
    )


_check_hotfix()
