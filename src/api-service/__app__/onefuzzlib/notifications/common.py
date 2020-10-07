#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import Optional

from jinja2.sandbox import SandboxedEnvironment
from onefuzztypes.models import Report

from ..azure.containers import auth_download_url
from ..azure.creds import get_instance_url
from ..jobs import Job
from ..tasks.config import get_setup_container
from ..tasks.main import Task


class Render:
    def __init__(self, container: str, filename: str, report: Report):
        self.report = report
        self.container = container
        self.filename = filename
        task = Task.get(report.job_id, report.task_id)
        if not task:
            raise ValueError
        job = Job.get(report.job_id)
        if not job:
            raise ValueError

        self.task_config = task.config
        self.job_config = job.config
        self.env = SandboxedEnvironment()

        self.target_url: Optional[str] = None
        setup_container = get_setup_container(task.config)
        if setup_container:
            self.target_url = auth_download_url(
                setup_container, self.report.executable.replace("setup/", "", 1)
            )

        self.report_url = auth_download_url(container, filename)
        self.input_url: Optional[str] = None
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
