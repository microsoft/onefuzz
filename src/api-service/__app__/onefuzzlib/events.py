#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
from queue import Empty, Queue
from typing import Optional

from onefuzztypes.events import Event, EventMessage, get_event_type

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
    event_message = EventMessage(event_type=event_type, event=event)
    EVENTS.put(event_message)
    Webhook.send_event(event_message)
