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
from .defaults import TEMPLATES
from .render import build_input_config, render


class JobTemplateIndex(BASE_INDEX, ORMMixin):
    @classmethod
    def key_fields(cls) -> Tuple[str, Optional[str]]:
        return ("name", None)

    @classmethod
    def get_base_entry(cls, name: str) -> Optional[BASE_INDEX]:
        result = cls.get(name)
        if result is not None:
            return BASE_INDEX(name=name, template=result.template)

        template = TEMPLATES.get(name)
        if template is None:
            return None

        return BASE_INDEX(name=name, template=template)

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

    @classmethod
    def execute(cls, request: JobTemplateRequest, user_info: UserInfo) -> Result[Job]:
        index = cls.get(request.name)
        if index is None:
            if request.name not in TEMPLATES:
                return Error(
                    code=ErrorCode.INVALID_REQUEST,
                    errors=["no such template: %s" % request.name],
                )
            base_template = TEMPLATES[request.name]
        else:
            base_template = index.template

        template = render(request, base_template)
        if isinstance(template, Error):
            return template

        try:
            for task_config in template.tasks:
                check_config(task_config)
                if task_config.pool is None:
                    return Error(
                        code=ErrorCode.INVALID_REQUEST, errors=["pool not defined"]
                    )

        except TaskConfigError as err:
            return Error(code=ErrorCode.INVALID_REQUEST, errors=[str(err)])

        for notification_config in template.notifications:
            for task_container in request.containers:
                if task_container.type == notification_config.container_type:
                    notification = Notification.create(
                        task_container.name,
                        notification_config.notification.config,
                        True,
                    )
                    if isinstance(notification, Error):
                        return notification

        job = Job(config=template.job)
        job.save()

        tasks: List[Task] = []
        for task_config in template.tasks:
            task_config.job_id = job.job_id
            if task_config.prereq_tasks:
                # pydantic verifies prereq_tasks in u128 form are index refs to
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
