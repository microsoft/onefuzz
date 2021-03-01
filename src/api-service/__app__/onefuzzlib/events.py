#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
from queue import Empty, Queue
from typing import Optional

from onefuzztypes.events import Event, EventMessage, get_event_type

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


def send_event(event: Event) -> None:
    event_type = get_event_type(event)

    extra = {
        "event_type": event_type,
        "custom_dimensions": json.loads(event.json(exclude_none=True)),
    }
    logging.info("event:%s", event_type, extra=extra)

    event_message = EventMessage(
        event_type=event_type,
        event=event,
        instance_id=get_instance_id(),
        instance_name=get_instance_name(),
    )
    EVENTS.put(event_message)
    Webhook.send_event(event_message)
