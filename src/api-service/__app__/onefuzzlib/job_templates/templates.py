#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from typing import List, Optional, Tuple

from onefuzztypes.enums import ErrorCode
from onefuzztypes.job_templates import JobTemplateConfig
from onefuzztypes.job_templates import JobTemplateIndex as BASE_INDEX
from onefuzztypes.job_templates import JobTemplateRequest
from onefuzztypes.models import Error, Result, UserInfo

from ..jobs import Job
from ..notifications.main import Notification
from ..orm import ORMMixin
from ..tasks.config import TaskConfigError, check_config
from ..tasks.main import Task
from .default_templates import TEMPLATES
from .render import build_input_config, render


class JobTemplateIndex(BASE_INDEX, ORMMixin):
    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        return ("name", None)

    @classmethod
    def get_index(cls) -> List[BASE_INDEX]:
        entries = [BASE_INDEX(name=x.name, template=x.template) for x in cls.search()]

        # if the local install has replaced the built-in templates, skip over them
        for name, template in TEMPLATES.items():
            if any(x.name == name for x in entries):
                continue
            entries.append(BASE_INDEX(name=name, template=template))

        return entries

    @classmethod
    def get_configs(cls) -> List[JobTemplateConfig]:
        configs = [build_input_config(x.name, x.template) for x in cls.get_index()]

        return configs

    def execute(self, request: JobTemplateRequest, user_info: UserInfo) -> Result[Job]:
        template = render(request, self.template)

        try:
            for task_config in template.tasks:
                check_config(task_config)
                if task_config.pool is None:
                    raise TaskConfigError("pool not defined")
        except TaskConfigError as err:
            return Error(code=ErrorCode.INVALID_REQUEST, errors=[str(err)])

        for notification_config in template.notifications:
            for task_container in request.containers:
                if task_container.type == notification_config.container_type:
                    notification = Notification.create(
                        task_container.name, notification_config.notification.config
                    )
                    if isinstance(notification, Error):
                        return notification

        job = Job(config=template.job)
        job.save()

        tasks: List[Task] = []
        for task_config in template.tasks:
            task_config.job_id = job.job_id
            if task_config.prereq_tasks:
                # the model checker verifies prereq_tasks in u128 form are index refs to
                # previously generated tasks
                task_config.prereq_tasks = [
                    tasks[x.int].task_id for x in task_config.prereq_tasks
                ]

            task = Task.create(
                config=task_config, job_id=job.job_id, user_info=user_info
            )
            if isinstance(task, Error):
                return task

            tasks.append(task)

        return job
