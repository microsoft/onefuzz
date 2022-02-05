#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from datetime import datetime
from enum import Enum
from typing import List, Optional
from uuid import UUID, uuid4

from pydantic import AnyHttpUrl, BaseModel, Field

from .enums import WebhookMessageState
from .events import Event, EventMessage, EventType


class WebhookMessageFormat(Enum):
    onefuzz = "onefuzz"
    event_grid = "event_grid"


class WebhookMessage(EventMessage):
    webhook_id: UUID


class WebhookMessageEventGrid(BaseModel):
    dataVersion: str
    subject: str
    eventType: EventType
    eventTime: datetime
    id: UUID
    data: Event


class WebhookMessageLog(WebhookMessage):
    state: WebhookMessageState = Field(default=WebhookMessageState.queued)
    try_count: int = Field(default=0)


class Webhook(BaseModel):
    webhook_id: UUID = Field(default_factory=uuid4)
    name: str
    url: Optional[AnyHttpUrl]
    event_types: List[EventType]
    secret_token: Optional[str]
    message_format: Optional[WebhookMessageFormat]
