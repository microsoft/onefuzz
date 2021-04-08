#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Dict, List, Optional
from uuid import UUID

from pydantic import AnyHttpUrl, BaseModel, Field, validator

from .consts import ONE_HOUR, SEVEN_DAYS
from .enums import (
    OS,
    Architecture,
    JobState,
    NodeState,
    PoolState,
    ScalesetState,
    TaskState,
)
from .events import EventType
from .models import AutoScaleConfig, NotificationConfig
from .primitives import Container, PoolName, Region


class BaseRequest(BaseModel):
    pass


class JobGet(BaseRequest):
    job_id: UUID


class JobSearch(BaseRequest):
    job_id: Optional[UUID]
    state: Optional[List[JobState]]
    task_state: Optional[List[TaskState]]
    with_tasks: Optional[bool]


class NotificationCreate(BaseRequest, NotificationConfig):
    container: Container


class NotificationGet(BaseRequest):
    notification_id: UUID


class TaskGet(BaseRequest):
    task_id: UUID


class TaskSearch(BaseRequest):
    job_id: Optional[UUID]
    task_id: Optional[UUID]
    state: Optional[List[TaskState]]


class TaskResize(TaskGet):
    count: int

    @validator("count", allow_reuse=True)
    def check_count(cls, value: int) -> int:
        if value <= 0:
            raise ValueError("invalid count")
        return value


class NodeCommandGet(BaseRequest):
    machine_id: UUID


class NodeCommandDelete(BaseRequest):
    machine_id: UUID
    message_id: str


class AgentRegistrationGet(BaseRequest):
    machine_id: UUID


class AgentRegistrationPost(BaseRequest):
    pool_name: PoolName
    scaleset_id: Optional[UUID]
    machine_id: UUID
    version: str = Field(default="1.0.0")


class PoolCreate(BaseRequest):
    name: PoolName
    os: OS
    arch: Architecture
    managed: bool
    client_id: Optional[UUID]
    autoscale: Optional[AutoScaleConfig]


class PoolSearch(BaseRequest):
    pool_id: Optional[UUID]
    name: Optional[PoolName]
    state: Optional[List[PoolState]]


class PoolStop(BaseRequest):
    name: PoolName
    now: bool


class ProxyGet(BaseRequest):
    scaleset_id: UUID
    machine_id: UUID
    dst_port: int


class ProxyCreate(BaseRequest):
    scaleset_id: UUID
    machine_id: UUID
    dst_port: int
    duration: int

    @validator("duration", allow_reuse=True)
    def check_duration(cls, value: int) -> int:
        if value < ONE_HOUR or value > SEVEN_DAYS:
            raise ValueError("invalid duration")
        return value


class ProxyDelete(BaseRequest):
    scaleset_id: UUID
    machine_id: UUID
    dst_port: Optional[int]


class NodeSearch(BaseRequest):
    machine_id: Optional[UUID]
    state: Optional[List[NodeState]]
    scaleset_id: Optional[UUID]
    pool_name: Optional[PoolName]


class NodeGet(BaseRequest):
    machine_id: UUID


class NodeUpdate(BaseRequest):
    machine_id: UUID
    debug_keep_node: Optional[bool]


class ScalesetSearch(BaseRequest):
    scaleset_id: Optional[UUID]
    state: Optional[List[ScalesetState]]
    include_auth: bool = Field(default=False)


class ScalesetStop(BaseRequest):
    scaleset_id: UUID
    now: bool


class ScalesetUpdate(BaseRequest):
    scaleset_id: UUID
    size: Optional[int]

    @validator("size", allow_reuse=True)
    def check_optional_size(cls, value: Optional[int]) -> Optional[int]:
        if value is not None and value < 0:
            raise ValueError("invalid size")
        return value


class ScalesetCreate(BaseRequest):
    pool_name: PoolName
    vm_sku: str
    image: str
    region: Optional[Region]
    size: int
    spot_instances: bool
    ephemeral_os_disks: bool = Field(default=False)
    tags: Dict[str, str]

    @validator("size", allow_reuse=True)
    def check_size(cls, value: int) -> int:
        if value <= 0:
            raise ValueError("invalid size")
        return value


class ContainerGet(BaseRequest):
    name: Container


class ContainerCreate(BaseRequest):
    name: Container
    metadata: Optional[Dict[str, str]]


class ContainerDelete(BaseRequest):
    name: Container
    metadata: Optional[Dict[str, str]]


class ReproGet(BaseRequest):
    vm_id: Optional[UUID]


class ProxyReset(BaseRequest):
    region: Region


class CanScheduleRequest(BaseRequest):
    machine_id: UUID
    task_id: UUID


class WebhookCreate(BaseRequest):
    name: str
    url: AnyHttpUrl
    event_types: List[EventType]
    secret_token: Optional[str]


class WebhookSearch(BaseModel):
    webhook_id: Optional[UUID]


class WebhookGet(BaseModel):
    webhook_id: UUID


class WebhookUpdate(BaseModel):
    webhook_id: UUID
    name: Optional[str]
    event_types: Optional[List[EventType]]
    url: Optional[AnyHttpUrl]
    secret_token: Optional[str]


class NodeAddSshKey(BaseModel):
    machine_id: UUID
    public_key: str
