#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Optional

from jinja2.sandbox import SandboxedEnvironment
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error, Report
from onefuzztypes.primitives import Container

from ..azure.containers import auth_download_url
from ..azure.creds import get_instance_url
from ..jobs import Job
from ..tasks.config import get_setup_container
from ..tasks.main import Task


def fail_task(report: Report, error: Exception) -> None:
    logging.error(
        "notification failed: job_id:%s task_id:%s err:%s",
        report.job_id,
        report.task_id,
        error,
    )

    task = Task.get(report.job_id, report.task_id)
    if task:
        task.mark_failed(
            Error(
                code=ErrorCode.NOTIFICATION_FAILURE,
                errors=["notification failed", str(error)],
            )
        )


class Render:
    def __init__(
        self,
        container: Container,
        filename: str,
        report: Report,
        *,
        task: Optional[Task] = None,
        job: Optional[Job] = None,
        target_url: Optional[str] = None,
        input_url: Optional[str] = None,
        report_url: Optional[str] = None,
    ):
        self.report = report
        self.container = container
        self.filename = filename
        if not task:
            task = Task.get(report.job_id, report.task_id)
            if not task:
                raise ValueError(f"invalid task {report.task_id}")
        if not job:
            job = Job.get(report.job_id)
            if not job:
                raise ValueError(f"invalid job {report.job_id}")

        self.task_config = task.config
        self.job_config = job.config
        self.env = SandboxedEnvironment()

        self.target_url = target_url
        if not self.target_url:
            setup_container = get_setup_container(task.config)
            if setup_container:
                self.target_url = auth_download_url(
                    setup_container, self.report.executable.replace("setup/", "", 1)
                )

        if report_url:
            self.report_url = report_url
        else:
            self.report_url = auth_download_url(container, filename)

        self.input_url = input_url
        if not self.input_url:
            if self.report.input_blob:
                self.input_url = auth_download_url(
                    self.report.input_blob.container, self.report.input_blob.name
                )

    def render(self, template: str) -> str:
        return self.env.from_string(template).render(
            {
                "report": self.report,
                "task": self.task_config,
                "job": self.job_config,
                "report_url": self.report_url,
                "input_url": self.input_url,
                "target_url": self.target_url,
                "report_container": self.container,
                "report_filename": self.filename,
                "repro_cmd": "onefuzz --endpoint %s repro create_and_connect %s %s"
                % (get_instance_url(), self.container, self.filename),
            }
        )
