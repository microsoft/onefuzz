#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Dict, List, Optional
from uuid import UUID

from pydantic import BaseModel

from .models import Forward, NodeCommandEnvelope
from .primitives import Container, ContainerHoldName, Region


class BaseResponse(BaseModel):
    pass


class BoolResult(BaseResponse):
    result: bool


class ProxyGetResult(BaseResponse):
    ip: Optional[str]
    forward: Forward


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


class ContainerDetail(BaseResponse):
    name: Container
    metadata: Dict[str, str]
    holds: List[ContainerHoldName]


class ContainerInfo(BaseResponse):
    sas_url: str
    name: Container
    holds: Optional[List[ContainerHoldName]]
    metadata: Optional[Dict[str, str]]


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
