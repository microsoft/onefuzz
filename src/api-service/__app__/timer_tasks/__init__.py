#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.enums import JobState, TaskState

from ..onefuzzlib.jobs import Job
from ..onefuzzlib.orm import process_state_updates
from ..onefuzzlib.tasks.main import Task
from ..onefuzzlib.tasks.scheduler import schedule_tasks


def main(mytimer: func.TimerRequest) -> None:  # noqa: F841
    expired_tasks = Task.search_expired()
    for task in expired_tasks:
        logging.info(
            "stopping expired task. job_id:%s task_id:%s", task.job_id, task.task_id
        )
        task.mark_stopping()

    expired_jobs = Job.search_expired()
    for job in expired_jobs:
        logging.info("stopping expired job. job_id:%s", job.job_id)
        job.stopping()

    jobs = Job.search_states(states=JobState.needs_work())
    for job in jobs:
        logging.info("update job: %s", job.job_id)
        process_state_updates(job)

    tasks = Task.search_states(states=TaskState.needs_work())
    for task in tasks:
        logging.info("update task: %s", task.task_id)
        process_state_updates(task)

    schedule_tasks()

    Job.stop_never_started_jobs()
