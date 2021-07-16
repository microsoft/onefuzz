import datetime
import logging

import azure.functions as func
from onefuzztypes.enums import JobState, TaskState

from ..onefuzzlib.events import get_events
from ..onefuzzlib.jobs import Job
from ..onefuzzlib.notifications.main import Notification
from ..onefuzzlib.repro import Repro
from ..onefuzzlib.tasks.main import Task

RETENTION_POLICY = datetime.timedelta(minutes=(5))


def main(mytimer1: func.TimerRequest, dashboard: func.Out[str]) -> None:  # noqa: F841

    now = datetime.datetime.now(tz=datetime.timezone.utc)

    time_retained = now - RETENTION_POLICY

    time_filter = f"Timestamp lt datetime'{time_retained.isoformat()}'"

    # You have to do notification before task,
    # because editing the upn for tasks will change the timestamp
    for notification in Notification.search(raw_unchecked_filter=time_filter):
        logging.info("Retention Timer Notification Search")
        logging.info(
            "Found notification %s older than 18 months. Checking related tasks.",
            notification.notification_id,
        )
        container = notification.container
        timestamp_list = []
        for task in Task.search():
            container_str = str(task.config.containers)
            if container in container_str:
                # Need to make sure there isn't a task still using the container.
                if task.state == TaskState.stopped:
                    timestamp_list.append(task.timestamp)
                else:
                    timestamp_list = []
                    break

        if len(timestamp_list) != 0:
            youngest = max(
                dt
                for dt in timestamp_list
                if isinstance(dt, datetime.datetime) and dt < now
            )
            if youngest < time_retained:
                logging.info(
                    "All related tasks are older than 18 months."
                    + " Deleting Notification %s.",
                    notification.notification_id,
                )
                notification.delete()

    for job in Job.search(
        query={"state": [JobState.stopped]}, raw_unchecked_filter=time_filter
    ):
        logging.info("Retention Timer Job Search")
        if job.user_info is not None and job.user_info.upn is not None:
            logging.info(
                "Found job %s older than 18 months. Scrubbing user_info.",
                job.job_id,
            )
            job.user_info.upn = None
            job.save()

    for task in Task.search(
        query={"state": [TaskState.stopped]}, raw_unchecked_filter=time_filter
    ):
        logging.info("Retention Timer Task Search")
        if task.user_info is not None and task.user_info.upn is not None:
            logging.info(
                "Found task %s older than 18 months. Scrubbing user_info.",
                task.task_id,
            )
            task.user_info.upn = None
            task.save()

    for repro in Repro.search(raw_unchecked_filter=time_filter):
        logging.info("Retention Timer Repro Search")
        logging.info(repro)
        if repro.user_info is not None and repro.user_info.upn is not None:
            logging.info(
                "Found repro entry for task %s on node %s that is older "
                + "than 18 months. Scrubbing user_info.",
                repro.task_id,
                repro.vm_id,
            )
            repro.user_info.upn = None
            repro.save()

    events = get_events()
    if events:
        dashboard.set(events)
