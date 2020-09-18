#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import List, Optional, Tuple
from uuid import UUID

from onefuzztypes.models import TaskEvent as BASE_TASK_EVENT
from onefuzztypes.models import (
    TaskEventSummary,
    WorkerDoneEvent,
    WorkerEvent,
    WorkerRunningEvent,
)

from .orm import ORMMixin


class TaskEvent(BASE_TASK_EVENT, ORMMixin):
    @classmethod
    def get_summary(cls, task_id: UUID) -> List[TaskEventSummary]:
        events = cls.search(query={"task_id": [task_id]})
        events.sort(key=lambda e: e.Timestamp)

        return [
            TaskEventSummary(
                timestamp=e.Timestamp,
                event_data=cls.get_event_data(e.event_data),
                event_type=type(e.event_data.event).__name__,
            )
            for e in events
        ]

    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        return ("task_id", None)

    @classmethod
    def get_event_data(cls, worker_event: WorkerEvent) -> str:
        event = worker_event.event
        if isinstance(event, WorkerDoneEvent):
            return "exit status: %s" % event.exit_status
        elif isinstance(event, WorkerRunningEvent):
            return ""
        else:
            return "Unrecognized event: %s" % event
