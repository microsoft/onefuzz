#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import List, Optional

from onefuzztypes.events import Event, EventMessage, EventType, get_event_type
from onefuzztypes.models import UserInfo
from pydantic import BaseModel

from .azure.creds import get_instance_id, get_instance_name
from .azure.queue import send_message
from .azure.storage import StorageType
from .webhooks import Webhook


class SignalREvent(BaseModel):
    target: str
    arguments: List[EventMessage]


def queue_signalr_event(event_message: EventMessage) -> None:
    message = SignalREvent(target="events", arguments=[event_message]).json().encode()
    send_message("signalr-events", message, StorageType.config)


def get_events() -> Optional[str]:
    return None


def log_event(event: Event, event_type: EventType) -> None:
    scrubbed_event = filter_event(event)
    logging.info(
        "sending event: %s - %s", event_type, scrubbed_event.json(exclude_none=True)
    )


def filter_event(event: Event) -> BaseModel:
    clone_event = event.copy(deep=True)
    filtered_event = filter_event_recurse(clone_event)
    return filtered_event


def filter_event_recurse(entry: BaseModel) -> BaseModel:

    for field in entry.__fields__:
        field_data = getattr(entry, field)

        if isinstance(field_data, UserInfo):
            field_data = None
        elif isinstance(field_data, list):
            for (i, value) in enumerate(field_data):
                if isinstance(value, BaseModel):
                    field_data[i] = filter_event_recurse(value)
        elif isinstance(field_data, dict):
            for (key, value) in field_data.items():
                if isinstance(value, BaseModel):
                    field_data[key] = filter_event_recurse(value)
        elif isinstance(field_data, BaseModel):
            field_data = filter_event_recurse(field_data)

        setattr(entry, field, field_data)

    return entry


def send_event(event: Event) -> None:
    event_type = get_event_type(event)

    event_message = EventMessage(
        event_type=event_type,
        event=event.copy(deep=True),
        instance_id=get_instance_id(),
        instance_name=get_instance_name(),
    )

    # work around odd bug with Event Message creation.  See PR 939
    if event_message.event != event:
        event_message.event = event.copy(deep=True)

    queue_signalr_event(event_message)
    Webhook.send_event(event_message)
    log_event(event, event_type)
