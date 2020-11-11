from typing import List, Union
from uuid import UUID, uuid4

from pydantic import BaseModel, Field

from .enums import WebhookEventType, WebhookMessageState
from .models import Error, TaskConfig
from .responses import BaseResponse


class WebhookEventTaskStopped(BaseModel):
    job_id: UUID
    task_id: UUID


class WebhookEventTaskFailed(BaseModel):
    job_id: UUID
    task_id: UUID
    error: Error


class WebhookEventTaskCreated(BaseModel):
    job_id: UUID
    task_id: UUID
    config: TaskConfig


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
    url: str
    name: str
    event_types: List[WebhookEventType]

    def redact(self) -> None:
        self.url = "***"
