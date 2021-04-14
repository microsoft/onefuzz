#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
from typing import List, Optional, Tuple
from uuid import UUID

from onefuzztypes.models import TaskEvent as BASE_TASK_EVENT
from onefuzztypes.models import TaskEventSummary, WorkerEvent

from .orm import ORMMixin


class TaskEvent(BASE_TASK_EVENT, ORMMixin):
    @classmethod
    def get_summary(cls, task_id: UUID) -> List[TaskEventSummary]:
        events = cls.search(query={"task_id": [task_id]})
        # handle None case of Optional[e.timestamp], which shouldn't happen
        events.sort(key=lambda e: e.timestamp or datetime.datetime.max)

        return [
            TaskEventSummary(
                timestamp=e.timestamp,
                event_data=get_event_data(e.event_data),
                event_type=get_event_type(e.event_data),
            )
            for e in events
        ]

    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        return ("task_id", None)


def get_event_data(event: WorkerEvent) -> str:
    if event.done:
        return "exit status: %s" % event.done.exit_status
    elif event.running:
        return ""
    else:
        return "Unrecognized event: %s" % event


def get_event_type(event: WorkerEvent) -> str:
    if event.done:
        return type(event.done).__name__
    elif event.running:
        return type(event.running).__name__
    else:
        return "Unrecognized event: %s" % event
