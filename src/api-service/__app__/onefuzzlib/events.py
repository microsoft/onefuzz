#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
from queue import Empty, Queue
from typing import Optional

from onefuzztypes.events import Event, EventMessage, EventType, get_event_type
from onefuzztypes.models import UserInfo
from pydantic import BaseModel

from .azure.creds import get_instance_id, get_instance_name
from .webhooks import Webhook

EVENTS: Queue = Queue()


def get_events() -> Optional[str]:
    events = []

    for _ in range(5):
        try:
            event = EVENTS.get(block=False)
            events.append(json.loads(event.json(exclude_none=True)))
            EVENTS.task_done()
        except Empty:
            break

    if events:
        return json.dumps({"target": "events", "arguments": events})
    else:
        return None


def log_event(event: Event, event_type: EventType) -> None:
    scrubbed_event = filter_event(event, event_type)
    logging.info(
        "sending event: %s - %s", event_type, scrubbed_event.json(exclude_none=True)
    )


def filter_event(event: Event, event_type: EventType) -> BaseModel:
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
    log_event(event, event_type)
    event_message = EventMessage(
        event_type=event_type,
        event=event,
        instance_id=get_instance_id(),
        instance_name=get_instance_name(),
    )
    EVENTS.put(event_message)
    Webhook.send_event(event_message)
