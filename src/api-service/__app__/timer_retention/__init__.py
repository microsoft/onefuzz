#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import logging

import azure.functions as func
from onefuzztypes.enums import JobState, TaskState

from ..onefuzzlib.jobs import Job
from ..onefuzzlib.notifications.main import Notification
from ..onefuzzlib.repro import Repro
from ..onefuzzlib.tasks.main import Task

RETENTION_POLICY = datetime.timedelta(days=(18 * 30))
SEARCH_EXTENT = datetime.timedelta(days=(20 * 30))


def main(mytimer: func.TimerRequest) -> None:  # noqa: F841

    now = datetime.datetime.now(tz=datetime.timezone.utc)

    time_retained_older = now - RETENTION_POLICY
    time_retained_newer = now - SEARCH_EXTENT

    time_filter = (
        f"Timestamp lt datetime'{time_retained_older.isoformat()}' "
        f"and Timestamp gt datetime'{time_retained_newer.isoformat()}'"
    )
    time_filter_newer = f"Timestamp gt datetime'{time_retained_older.isoformat()}'"

    # Collecting 'still relevant' task containers.
    # NOTE: This must be done before potentially modifying tasks otherwise
    # the task timestamps will not be useful.
    used_containers = set()
    for task in Task.search(raw_unchecked_filter=time_filter_newer):
        task_containers = {x.name for x in task.config.containers}
        used_containers.update(task_containers)

    for notification in Notification.search(raw_unchecked_filter=time_filter):
        logging.debug(
            "checking expired notification for removal: %s",
            notification.notification_id,
        )
        container = notification.container
        if container not in used_containers:
            logging.info(
                "deleting expired notification: %s", notification.notification_id
            )
            notification.delete()

    for job in Job.search(
        query={"state": [JobState.stopped]}, raw_unchecked_filter=time_filter
    ):
        if job.user_info is not None and job.user_info.upn is not None:
            logging.info("removing PII from job: %s", job.job_id)
            job.user_info.upn = None
            job.save()

    for task in Task.search(
        query={"state": [TaskState.stopped]}, raw_unchecked_filter=time_filter
    ):
        if task.user_info is not None and task.user_info.upn is not None:
            logging.info("removing PII from task: %s", task.task_id)
            task.user_info.upn = None
            task.save()

    for repro in Repro.search(raw_unchecked_filter=time_filter):
        if repro.user_info is not None and repro.user_info.upn is not None:
            logging.info("removing PII from repro: %s", repro.vm_id)
            repro.user_info.upn = None
            repro.save()
