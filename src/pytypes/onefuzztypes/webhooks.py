#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import List, Optional
from uuid import UUID, uuid4

from pydantic import AnyHttpUrl, BaseModel, Field

from .enums import WebhookMessageState
from .events import Event, EventType


class WebhookMessage(BaseModel):
    webhook_id: UUID
    event_id: UUID = Field(default_factory=uuid4)
    event_type: EventType
    event: Event


class WebhookMessageLog(WebhookMessage):
    state: WebhookMessageState = Field(default=WebhookMessageState.queued)
    try_count: int = Field(default=0)


class Webhook(BaseModel):
    webhook_id: UUID = Field(default_factory=uuid4)
    name: str
    url: Optional[AnyHttpUrl]
    event_types: List[EventType]
    secret_token: Optional[str]
