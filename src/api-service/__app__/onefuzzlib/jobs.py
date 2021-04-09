#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from datetime import datetime, timedelta
from typing import List, Optional, Tuple

from onefuzztypes.enums import ErrorCode, JobState, TaskState
from onefuzztypes.events import EventJobCreated, EventJobStopped, JobTaskStopped
from onefuzztypes.models import Error
from onefuzztypes.models import Job as BASE_JOB

from .events import send_event
from .orm import MappingIntStrAny, ORMMixin, QueryFilter
from .tasks.main import Task

JOB_LOG_PREFIX = "jobs: "
JOB_NEVER_STARTED_DURATION: timedelta = timedelta(days=30)


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

    @classmethod
    def stop_never_started_jobs(cls) -> None:
        # Note, the "not(end_time...)" with end_time set long before the use of
        # OneFuzz enables identifying those without end_time being set.
        last_timestamp = (datetime.utcnow() - JOB_NEVER_STARTED_DURATION).isoformat()

        time_filter = (
            f"Timestamp lt datetime'{last_timestamp}' and "
            "not(end_time ge datetime'2000-01-11T00:00:00.0Z')"
        )

        for job in cls.search(
            query={
                "state": [JobState.enabled],
            },
            raw_unchecked_filter=time_filter,
        ):
            for task in Task.search(query={"job_id": [job.job_id]}):
                task.mark_failed(
                    Error(
                        code=ErrorCode.TASK_FAILED,
                        errors=["job never not start"],
                    )
                )

            logging.info(
                JOB_LOG_PREFIX + "stopping job that never started: %s", job.job_id
            )
            job.stopping()

    def save_exclude(self) -> Optional[MappingIntStrAny]:
        return {"task_info": ...}

    def telemetry_include(self) -> Optional[MappingIntStrAny]:
        return {
            "machine_id": ...,
            "state": ...,
            "scaleset_id": ...,
        }

    def init(self) -> None:
        logging.info(JOB_LOG_PREFIX + "init: %s", self.job_id)
        self.state = JobState.enabled
        self.save()

    def stop_if_all_done(self) -> None:
        not_stopped = [
            task
            for task in Task.search(query={"job_id": [self.job_id]})
            if task.state != TaskState.stopped
        ]
        if not_stopped:
            return

        logging.info(
            JOB_LOG_PREFIX + "stopping job as all tasks are stopped: %s", self.job_id
        )
        self.stopping()

    def stopping(self) -> None:
        self.state = JobState.stopping
        logging.info(JOB_LOG_PREFIX + "stopping: %s", self.job_id)
        tasks = Task.search(query={"job_id": [self.job_id]})
        not_stopped = [task for task in tasks if task.state != TaskState.stopped]

        if not_stopped:
            for task in not_stopped:
                task.mark_stopping()
        else:
            self.state = JobState.stopped
            task_info = [
                JobTaskStopped(
                    task_id=x.task_id, error=x.error, task_type=x.config.task.type
                )
                for x in tasks
            ]
            send_event(
                EventJobStopped(
                    job_id=self.job_id,
                    config=self.config,
                    user_info=self.user_info,
                    task_info=task_info,
                )
            )
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
