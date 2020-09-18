#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
from enum import Enum
from queue import Empty, Queue
from typing import Dict, Optional, Union
from uuid import UUID

from onefuzztypes.primitives import Event

EVENTS: Queue = Queue()


def resolve(data: Event) -> Union[str, int, Dict[str, str]]:
    if isinstance(data, str):
        return data
    if isinstance(data, UUID):
        return str(data)
    elif isinstance(data, Enum):
        return data.name
    elif isinstance(data, int):
        return data
    elif isinstance(data, dict):
        for x in data:
            data[x] = str(data[x])
        return data
    raise NotImplementedError("no conversion from %s" % type(data))


def get_event() -> Optional[str]:
    events = []

    for _ in range(10):
        try:
            (event, data) = EVENTS.get(block=False)
            events.append({"type": event, "data": data})
            EVENTS.task_done()
        except Empty:
            break

    if events:
        return json.dumps({"target": "dashboard", "arguments": events})
    else:
        return None


def add_event(message_type: str, data: Dict[str, Event]) -> None:
    for key in data:
        data[key] = resolve(data[key])

    EVENTS.put((message_type, data))
