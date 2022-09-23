#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Any, Dict, List, Optional
from uuid import UUID

from pydantic import AnyHttpUrl, BaseModel, Field, root_validator

from ._monkeypatch import _check_hotfix
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
from .models import AutoScaleConfig, InstanceConfig, NotificationConfig
from .primitives import Container, PoolName, Region
from .webhooks import WebhookMessageFormat


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
    replace_existing: bool = Field(default=False)


class NotificationSearch(BaseRequest):
    container: Optional[List[Container]]


class NotificationGet(BaseRequest):
    notification_id: UUID


class TaskGet(BaseRequest):
    task_id: UUID


class TaskSearch(BaseRequest):
    job_id: Optional[UUID]
    task_id: Optional[UUID]
    state: Optional[List[TaskState]]


class TaskResize(TaskGet):
    count: int = Field(ge=1)


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
    scaleset_id: Optional[UUID]
    machine_id: Optional[UUID]
    dst_port: Optional[int]

    @root_validator()
    def check_proxy_get(cls, value: Any) -> Any:
        check_keys = ["scaleset_id", "machine_id", "dst_port"]
        included = [x in value for x in check_keys]
        if any(included) and not all(included):
            raise ValueError(
                "ProxyGet must provide all or none of the following: %s"
                % ", ".join(check_keys)
            )
        return value


class ProxyCreate(BaseRequest):
    scaleset_id: UUID
    machine_id: UUID
    dst_port: int
    duration: int = Field(ge=ONE_HOUR, le=SEVEN_DAYS)


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
    size: Optional[int] = Field(ge=1)


class AutoScaleOptions(BaseModel):
    min: int = Field(ge=0)
    max: int = Field(ge=1)
    default: int = Field(ge=0)
    scale_out_amount: int = Field(ge=1)
    scale_out_cooldown: int = Field(ge=1)
    scale_in_amount: int = Field(ge=1)
    scale_in_cooldown: int = Field(ge=1)


class ScalesetCreate(BaseRequest):
    pool_name: PoolName
    vm_sku: str
    image: Optional[str]
    region: Optional[Region]
    size: int = Field(ge=1)
    spot_instances: bool
    ephemeral_os_disks: bool = Field(default=False)
    tags: Dict[str, str]
    auto_scale: Optional[AutoScaleOptions]


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
    message_format: Optional[WebhookMessageFormat]


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
    message_format: Optional[WebhookMessageFormat]


class NodeAddSshKey(BaseModel):
    machine_id: UUID
    public_key: str


class InstanceConfigUpdate(BaseModel):
    config: InstanceConfig


_check_hotfix()
