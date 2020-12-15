#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

from typing import Optional
from uuid import UUID
import json
from onefuzztypes.primitives import Region
from onefuzztypes.enums import (
    TaskType,
    ContainerType,
    ErrorCode,
    OS,
    Architecture,
)
from onefuzztypes.models import (
    TaskConfig,
    TaskDetails,
    TaskContainers,
    Error,
    UserInfo,
    JobConfig,
)
from onefuzztypes.events import (
    Event,
    EventPing,
    EventTaskCreated,
    EventTaskStopped,
    EventTaskFailed,
    EventProxyCreated,
    EventProxyDeleted,
    EventProxyFailed,
    EventPoolCreated,
    EventPoolDeleted,
    EventScalesetCreated,
    EventScalesetFailed,
    EventScalesetDeleted,
    EventJobCreated,
    get_event_type,
    EventType,
)
from onefuzztypes.webhooks import WebhookMessage


def layer(depth: int, title: str, content: Optional[str] = None) -> None:
    print(f"{'#' * depth} {title}\n")
    if content is not None:
        print(f"{content}\n")


def typed(depth: int, title: str, content: str, data_type: str) -> None:
    print(f"{'#' * depth} {title}\n\n```{data_type}\n{content}\n```\n")


def main():
    examples = [
        EventPing(ping_id=UUID(int=0)),
        EventTaskCreated(
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
        EventTaskStopped(
            job_id=UUID(int=0),
            task_id=UUID(int=0),
            user_info=UserInfo(
                application_id=UUID(int=0),
                object_id=UUID(int=0),
                upn="example@contoso.com",
            ),
        ),
        EventTaskFailed(
            job_id=UUID(int=0),
            task_id=UUID(int=0),
            error=Error(code=ErrorCode.TASK_FAILED, errors=["example error message"]),
            user_info=UserInfo(
                application_id=UUID(int=0),
                object_id=UUID(int=0),
                upn="example@contoso.com",
            ),
        ),
        EventProxyCreated(region=Region("eastus")),
        EventProxyDeleted(region=Region("eastus")),
        EventProxyFailed(
            region=Region("eastus"),
            error=Error(code=ErrorCode.PROXY_FAILED, errors=["example error message"]),
        ),
        EventPoolCreated(
            pool_name="example", os=OS.linux, arch=Architecture.x86_64, managed=True
        ),
        EventPoolDeleted(pool_name="example"),
        EventScalesetCreated(
            scaleset_id=UUID(int=0),
            pool_name="example",
            vm_sku="Standard_D2s_v3",
            image="Canonical:UbuntuServer:18.04-LTS:latest",
            region=Region("eastus"),
            size=10,
        ),
        EventScalesetFailed(
            scaleset_id=UUID(int=0),
            pool_name="example",
            error=Error(
                code=ErrorCode.UNABLE_TO_RESIZE, errors=["example error message"]
            ),
        ),
        EventScalesetDeleted(scaleset_id=UUID(int=0), pool_name="example"),
        EventJobCreated(
            job_id=UUID(int=0),
            config=JobConfig(
                project="example project",
                name="example name",
                build="build 1",
                duration=24,
            ),
        ),
    ]

    for event in Event.__args__:
        seen = False
        for value in examples:
            if isinstance(value, event):
                seen = True
                break
        assert seen, "missing event type definition: %s" % event.__name__

    event_types = [get_event_type(x) for x in examples]

    for event_type in EventType:
        assert event_type in event_types, (
            "missing event type definition: %s" % event_type.name
        )

    message = WebhookMessage(
        webhook_id=UUID(int=0),
        event_id=UUID(int=0),
        event_type=EventType.ping,
        event=EventPing(ping_id=UUID(int=0)),
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
    layer(2, "Event Types (EventType)")

    for example in examples:
        webhook_type = get_event_type(example)
        print(f"* [{webhook_type.name}](#{webhook_type.name})")
    print()

    for example in examples:
        webhook_type = get_event_type(example)
        layer(3, webhook_type.name)
        typed(4, "Example", example.json(indent=4, exclude_none=True), "json")
        typed(4, "Schema", example.schema_json(indent=4), "json")

    typed(2, "Full Event Schema", message.schema_json(indent=4), "json")


if __name__ == "__main__":
    main()
