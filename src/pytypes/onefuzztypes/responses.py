#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Dict, List, Optional
from uuid import UUID

from pydantic import BaseModel

from .enums import VmState
from .models import Forward, NodeCommandEnvelope
from .primitives import Region


class BaseResponse(BaseModel):
    pass


class BoolResult(BaseResponse):
    result: bool


class ProxyGetResult(BaseResponse):
    ip: Optional[str]
    forward: Forward


class ProxyInfo(BaseModel):
    region: Region
    proxy_id: UUID
    state: VmState


class ProxyList(BaseResponse):
    proxies: List[ProxyInfo]


class Version(BaseResponse):
    git: str
    build: str
    version: str


class Info(BaseResponse):
    resource_group: str
    region: Region
    subscription: str
    versions: Dict[str, Version]
    instance_id: Optional[UUID]
    insights_appid: Optional[str]
    insights_instrumentation_key: Optional[str]


class ContainerInfoBase(BaseResponse):
    name: str
    metadata: Optional[Dict[str, str]]


class ContainerInfo(ContainerInfoBase):
    sas_url: str


class TestData(BaseResponse):
    data: str


class AgentRegistration(BaseResponse):
    events_url: str
    work_queue: str
    commands_url: str


class PendingNodeCommand(BaseResponse):
    envelope: Optional[NodeCommandEnvelope]


class CanSchedule(BaseResponse):
    allowed: bool
    work_stopped: bool
