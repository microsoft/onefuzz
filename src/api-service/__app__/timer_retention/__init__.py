import datetime
import logging

import azure.functions as func
from onefuzztypes.enums import JobState

from ..onefuzzlib.jobs import Job
from ..onefuzzlib.notifications.main import Notification
from ..onefuzzlib.tasks.main import Task

RETENTION_POLICY = datetime.timedelta(days=(18))


def main(mytimer: func.TimerRequest) -> None:
    utc_timestamp = (
        datetime.datetime.utcnow().replace(tzinfo=datetime.timezone.utc).isoformat()
    )

    for job in Job.search():
        logging.info("Retention Timer Job Search")
        job_timestamp = job.timestamp
        if job_timestamp < (
            datetime.datetime.now(tz=datetime.timezone.utc) - RETENTION_POLICY
        ):
            logging.info(
                "Found job %s older than 18 months. Scrubbing user_info.", job.job_id
            )
            job.user_info.upn = "noreply@microsoft.com"
            job.save()

    for task in Task.search():
        logging.info("Retention Timer Task Search")
        task_timestamp = task.timestamp
        if task_timestamp < (
            datetime.datetime.now(tz=datetime.timezone.utc) - RETENTION_POLICY
        ):
            logging.info(
                "Found task %s older than 18 months. Scrubbing user_info.", task.task_id
            )
            task.user_info.upn = "noreply@microsoft.com"
            task.save()

    for notification in Notification.search():
        logging.info("Retention Timer Notification Search")
        logging.info(notification)
        logging.info(notification.config.ado_fields["System.AssignedTo"])
        logging.info(notification.timestamp)
        # notification_timestamp = notification.timestamp
        # if notification_timestamp < (
        #         datetime.datetime.now(tz=datetime.timezone.utc) - RETENTION_POLICY
        #     ):
        #     logging.info("Found notification %s older than 18 months. Scrubbing user_info.", notification.notification_id)
        #     logging.info(notification.config.ado_fields)
        # notification.config.ado_fields = "noreply@microsoft.com"
        # notification.save()
