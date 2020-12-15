#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from datetime import datetime, timedelta
from typing import List, Optional, Tuple

from onefuzztypes.enums import JobState, TaskState
from onefuzztypes.events import EventJobCreated
from onefuzztypes.models import Job as BASE_JOB

from .events import send_event
from .orm import MappingIntStrAny, ORMMixin, QueryFilter
from .tasks.main import Task


class Job(BASE_JOB, ORMMixin):
    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        return ("job_id", None)

    @classmethod
    def search_states(cls, *, states: Optional[List[JobState]] = None) -> List["Job"]:
        query: QueryFilter = {}
        if states:
            query["state"] = states
        return cls.search(query=query)

    @classmethod
    def search_expired(cls) -> List["Job"]:
        time_filter = "end_time lt datetime'%s'" % datetime.utcnow().isoformat()

        return cls.search(
            query={"state": JobState.available()}, raw_unchecked_filter=time_filter
        )

    def save_exclude(self) -> Optional[MappingIntStrAny]:
        return {"task_info": ...}

    def telemetry_include(self) -> Optional[MappingIntStrAny]:
        return {
            "machine_id": ...,
            "state": ...,
            "scaleset_id": ...,
        }

    def init(self) -> None:
        logging.info("init job: %s", self.job_id)
        self.state = JobState.enabled
        self.save()

    def stopping(self) -> None:
        self.state = JobState.stopping
        logging.info("stopping job: %s", self.job_id)
        not_stopped = [
            task
            for task in Task.search(query={"job_id": [self.job_id]})
            if task.state != TaskState.stopped
        ]

        if not_stopped:
            for task in not_stopped:
                task.mark_stopping()
        else:
            self.state = JobState.stopped
        self.save()

    def on_start(self) -> None:
        # try to keep this effectively idempotent
        if self.end_time is None:
            self.end_time = datetime.utcnow() + timedelta(hours=self.config.duration)
            self.save()

    def save(self, new: bool = False, require_etag: bool = False) -> None:
        created = self.etag is None
        super().save(new=new, require_etag=require_etag)

        if created:
            send_event(
                EventJobCreated(
                    job_id=self.job_id, config=self.config, user_info=self.user_info
                )
            )
