from typing import List, Union
from uuid import UUID, uuid4

from pydantic import BaseModel, Field

from .enums import WebhookEventType, WebhookMessageState
from .models import TaskConfig


class WebhookEventTaskCreated(BaseModel):
    event_id: UUID
    job_id: UUID
    task_id: UUID
    task_config: TaskConfig


WebhookEvent = Union[WebhookEventTaskCreated]


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
    url: str
    event_types: List[WebhookEventType]
