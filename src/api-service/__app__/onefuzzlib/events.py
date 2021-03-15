#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
from queue import Empty, Queue
from typing import List, Optional, Set

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
    logging.info("sending event: %s - %s", event_type, scrubbed_event)


def filter_event(event: Event, event_type: EventType) -> Event:

    clone_event = event.copy(deep=True)
    filter_event_recurs(clone_event)

    return clone_event


def filter_event_recurs(clone_event: BaseModel, visited: Set[int] = set()) -> BaseModel:

    if id(clone_event) in visited:
        return clone_event

    visited.add(id(clone_event))

    for field in clone_event.__fields__:
        field_data = getattr(clone_event, field)

        if isinstance(field_data, UserInfo):

            field_data = None

        elif isinstance(field_data, List):

            if len(field_data) > 0 and not isinstance(field_data[0], BaseModel):
                continue
            for data in field_data:
                filter_event_recurs(data, visited)

        elif isinstance(field_data, dict):

            for key in field_data:
                if not isinstance(field_data[key], BaseModel):
                    continue
                filter_event_recurs(field_data[key], visited)

        else:

            if isinstance(field_data, BaseModel):
                filter_event_recurs(field_data, visited)

        setattr(clone_event, field, field_data)

    return clone_event


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
