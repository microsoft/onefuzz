#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.enums import ErrorCode, JobState
from onefuzztypes.models import Error, TaskConfig
from onefuzztypes.requests import TaskGet, TaskSearch
from onefuzztypes.responses import BoolResult

from ..onefuzzlib.endpoint_authorization import call_if_user
from ..onefuzzlib.jobs import Job
from ..onefuzzlib.request import not_ok, ok, parse_request
from ..onefuzzlib.task_event import TaskEvent
from ..onefuzzlib.tasks.config import TaskConfigError, check_config
from ..onefuzzlib.tasks.main import Task
from ..onefuzzlib.user_credentials import parse_jwt_token
from ..onefuzzlib.workers.nodes import NodeTasks


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(TaskConfig, req)
    if isinstance(request, Error):
        return not_ok(request, context="task create")

    user_info = parse_jwt_token(req)
    if isinstance(user_info, Error):
        return not_ok(user_info, context="task create")

    try:
        check_config(request)
    except TaskConfigError as err:
        return not_ok(
            Error(code=ErrorCode.INVALID_REQUEST, errors=[str(err)]),
            context="task create",
        )

    if "dryrun" in req.params:
        return ok(BoolResult(result=True))

    job = Job.get(request.job_id)
    if job is None:
        return not_ok(
            Error(code=ErrorCode.INVALID_REQUEST, errors=["unable to find job"]),
            context=request.job_id,
        )

    if job.state not in [JobState.enabled, JobState.init]:
        return not_ok(
            Error(
                code=ErrorCode.UNABLE_TO_ADD_TASK_TO_JOB,
                errors=["unable to add a job in state: %s" % job.state.name],
            ),
            context=job.job_id,
        )

    if request.prereq_tasks:
        for task_id in request.prereq_tasks:
            prereq = Task.get_by_task_id(task_id)
            if isinstance(prereq, Error):
                return not_ok(prereq, context="task create prerequisite")

    task = Task.create(config=request, job_id=request.job_id, user_info=user_info)
    if isinstance(task, Error):
        return not_ok(task, context="task create invalid pool")
    return ok(task)


def get(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(TaskSearch, req)
    if isinstance(request, Error):
        return not_ok(request, context="task get")

    if request.task_id:
        task = Task.get_by_task_id(request.task_id)
        if isinstance(task, Error):
            return not_ok(task, context=request.task_id)
        task.nodes = NodeTasks.get_node_assignments(request.task_id)
        task.events = TaskEvent.get_summary(request.task_id)
        return ok(task)

    tasks = Task.search_states(states=request.state, job_id=request.job_id)
    return ok(tasks)


def delete(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(TaskGet, req)
    if isinstance(request, Error):
        return not_ok(request, context="task delete")

    task = Task.get_by_task_id(request.task_id)
    if isinstance(task, Error):
        return not_ok(task, context=request.task_id)

    task.mark_stopping()

    return ok(task)


def main(req: func.HttpRequest) -> func.HttpResponse:
    methods = {"GET": get, "POST": post, "DELETE": delete}
    method = methods[req.method]
    result = call_if_user(req, method)

    return result
