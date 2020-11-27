#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import List, Optional, Union
from uuid import UUID, uuid4

from pydantic import AnyHttpUrl, BaseModel, Field

from .enums import WebhookEventType, WebhookMessageState
from .models import Error, TaskConfig, UserInfo
from .responses import BaseResponse


class WebhookEventTaskStopped(BaseModel):
    job_id: UUID
    task_id: UUID
    user_info: Optional[UserInfo]


class WebhookEventTaskFailed(BaseModel):
    job_id: UUID
    task_id: UUID
    error: Error
    user_info: Optional[UserInfo]


class WebhookEventTaskCreated(BaseModel):
    job_id: UUID
    task_id: UUID
    config: TaskConfig
    user_info: Optional[UserInfo]


class WebhookEventPing(BaseResponse):
    ping_id: UUID = Field(default_factory=uuid4)


WebhookEvent = Union[
    WebhookEventTaskCreated,
    WebhookEventTaskStopped,
    WebhookEventTaskFailed,
    WebhookEventPing,
]


class WebhookMessage(BaseModel):
    webhook_id: UUID
    event_id: UUID = Field(default_factory=uuid4)
    event_type: WebhookEventType
    event: WebhookEvent


class WebhookMessageLog(WebhookMessage):
    state: WebhookMessageState = Field(default=WebhookMessageState.queued)
    try_count: int = Field(default=0)


class Webhook(BaseModel):
    webhook_id: UUID = Field(default_factory=uuid4)
    name: str
    url: Optional[AnyHttpUrl]
    event_types: List[WebhookEventType]
    secret_token: Optional[str]
