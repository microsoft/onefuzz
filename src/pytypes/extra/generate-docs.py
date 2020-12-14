#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

import json
from typing import Optional
from uuid import UUID

from onefuzztypes.enums import ContainerType, ErrorCode, TaskType, WebhookEventType
from onefuzztypes.models import (
    BlobRef,
    Error,
    Report,
    TaskConfig,
    TaskContainers,
    TaskDetails,
    UserInfo,
)
from onefuzztypes.primitives import Container
from onefuzztypes.webhooks import (
    WebhookEvent,
    WebhookEventCrashReportCreated,
    WebhookEventPing,
    WebhookEventTaskCreated,
    WebhookEventTaskFailed,
    WebhookEventTaskStopped,
    WebhookMessage,
)

EMPTY_SHA256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
ZERO_SHA256 = "0" * len(EMPTY_SHA256)


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
            job_id=UUID(int=0),
            task_id=UUID(int=0),
            user_info=UserInfo(
                application_id=UUID(int=0),
                object_id=UUID(int=0),
                upn="example@contoso.com",
            ),
        ),
        WebhookEventType.task_failed: WebhookEventTaskFailed(
            job_id=UUID(int=0),
            task_id=UUID(int=0),
            error=Error(code=ErrorCode.TASK_FAILED, errors=["example error message"]),
            user_info=UserInfo(
                application_id=UUID(int=0),
                object_id=UUID(int=0),
                upn="example@contoso.com",
            ),
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
            user_info=UserInfo(
                application_id=UUID(int=0),
                object_id=UUID(int=0),
                upn="example@contoso.com",
            ),
        ),
        WebhookEventType.crash_report_created: WebhookEventCrashReportCreated(
            container=Container("container-name"),
            filename="example.json",
            report=Report(
                input_blob=BlobRef(
                    account="contoso-storage-account",
                    container=Container("crashes"),
                    name="input.txt",
                ),
                executable="fuzz.exe",
                crash_type="example crash report type",
                crash_site="example crash site",
                call_stack=["#0 line", "#1 line", "#2 line"],
                call_stack_sha256=ZERO_SHA256,
                input_sha256=EMPTY_SHA256,
                asan_log="example asan log",
                task_id=UUID(int=0),
                job_id=UUID(int=0),
                scariness_score=10,
                scariness_description="example-scariness",
            ),
        ),
    }

    for entry in WebhookEventType:
        assert entry in examples, "missing event type: %s" % entry

    for event in WebhookEvent.__args__:
        seen = False
        for value in examples.values():
            if isinstance(value, event):
                seen = True
        assert seen, "missikng event type definition: %s" % event.__name__

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
