#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
from queue import Empty, Queue
from typing import Optional

from onefuzztypes.events import Event, get_event_type

from .webhooks import Webhook

EVENTS: Queue = Queue()


def get_events() -> Optional[str]:
    events = []

    for _ in range(5):
        try:
            (event, data) = EVENTS.get(block=False)
            events.append(
                {"type": event, "data": json.loads(data.json(exclude_none=True))}
            )
            EVENTS.task_done()
        except Empty:
            break

    if events:
        return json.dumps({"target": "events", "arguments": events})
    else:
        return None


def send_event(event: Event) -> None:
    event_type = get_event_type(event)
    EVENTS.put((event_type, event))
    Webhook.send_event(event_type=event_type, event=event)
