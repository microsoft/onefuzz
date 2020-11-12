#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

from typing import Optional
from uuid import UUID
import json
from onefuzztypes.enums import TaskType, ContainerType, ErrorCode
from onefuzztypes.models import TaskConfig, TaskDetails, TaskContainers, Error
from onefuzztypes.webhooks import (
    WebhookMessage,
    WebhookEventPing,
    WebhookEventTaskCreated,
    WebhookEventTaskStopped,
    WebhookEventTaskFailed,
)
from onefuzztypes.enums import WebhookEventType


def layer(depth: int, title: str, content: Optional[str] = None) -> None:
    print(f"{'#' * depth} {title}\n")
    if content is not None:
        print(f"{content}\n")


def typed(depth: int, title: str, content: str, data_type: str) -> None:
    print(f"{'#' * depth} {title}\n\n```{data_type}\n{content}\n```\n")


def main():
    examples = {
        WebhookEventType.ping: WebhookEventPing(ping_id=UUID(int=0)),
        WebhookEventType.task_stopped: WebhookEventTaskStopped(
            job_id=UUID(int=0), task_id=UUID(int=0)
        ),
        WebhookEventType.task_failed: WebhookEventTaskFailed(
            job_id=UUID(int=0),
            task_id=UUID(int=0),
            error=Error(code=ErrorCode.TASK_FAILED, errors=["example error message"]),
        ),
        WebhookEventType.task_created: WebhookEventTaskCreated(
            job_id=UUID(int=0),
            task_id=UUID(int=0),
            config=TaskConfig(
                job_id=UUID(int=0),
                task=TaskDetails(
                    type=TaskType.libfuzzer_fuzz,
                    duration=1,
                    target_exe="fuzz.exe",
                    target_env={},
                    target_options=[],
                ),
                containers=[
                    TaskContainers(name="my-setup", type=ContainerType.setup),
                    TaskContainers(name="my-inputs", type=ContainerType.inputs),
                    TaskContainers(name="my-crashes", type=ContainerType.crashes),
                ],
                tags={},
            ),
        ),
    }

    message = WebhookMessage(
        webhook_id=UUID(int=0),
        event_id=UUID(int=0),
        event_type=WebhookEventType.ping,
        event=examples[WebhookEventType.ping],
    )

    layer(
        1,
        "Webhook Events",
        "This document describes the basic webhook event subscriptions available in OneFuzz",
    )
    layer(
        2,
        "Payload",
        "Each event will be submitted via HTTP POST to the user provided URL.",
    )

    typed(3, "Example", message.json(indent=4, exclude_none=True), "json")
    layer(2, "Event Types (WebhookEventType)")

    for webhook_type in WebhookEventType:
        example = examples[webhook_type]
        layer(3, webhook_type.name)
        typed(4, "Example", example.json(indent=4, exclude_none=True), "json")
        typed(4, "Schema", example.schema_json(indent=4), "json")

    typed(2, "Full Event Schema", message.schema_json(indent=4), "json")


if __name__ == "__main__":
    main()
