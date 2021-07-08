import datetime
import logging

import azure.functions as func

from ..onefuzzlib.jobs import Job
from ..onefuzzlib.notifications.main import Notification
from ..onefuzzlib.tasks.main import Task

RETENTION_POLICY = datetime.timedelta(days=(18))


def main(mytimer: func.TimerRequest) -> None:

    for job in Job.search():
        logging.info("Retention Timer Job Search")
        job_timestamp = job.timestamp
        if job_timestamp is not None and job_timestamp < (
            datetime.datetime.now(tz=datetime.timezone.utc) - RETENTION_POLICY
        ):
            if job.user_info is not None:
                logging.info(
                    "Found job %s older than 18 months. Scrubbing user_info.",
                    job.job_id,
                )
                job.user_info.upn = "noreply@microsoft.com"
                job.save()

    for task in Task.search():
        logging.info("Retention Timer Task Search")
        task_timestamp = task.timestamp
        if task_timestamp is not None and task_timestamp < (
            datetime.datetime.now(tz=datetime.timezone.utc) - RETENTION_POLICY
        ):
            if task.user_info is not None:
                logging.info(
                    "Found task %s older than 18 months. Scrubbing user_info.",
                    task.task_id,
                )
                task.user_info.upn = "noreply@microsoft.com"
                task.save()

    for notification in Notification.search():
        logging.info("Retention Timer Notification Search")
        logging.info(notification)
        # logging.info(notification.config.ado_fields["System.AssignedTo"])
        logging.info(notification.timestamp)
