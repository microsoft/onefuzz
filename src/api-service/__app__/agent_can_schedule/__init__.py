#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.enums import ErrorCode, TaskState
from onefuzztypes.models import Error
from onefuzztypes.requests import CanScheduleRequest
from onefuzztypes.responses import CanSchedule

from ..onefuzzlib.agent_authorization import verify_token
from ..onefuzzlib.pools import Node
from ..onefuzzlib.request import not_ok, ok, parse_request
from ..onefuzzlib.tasks.main import Task


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(CanScheduleRequest, req)
    if isinstance(request, Error):
        return not_ok(request, context="CanScheduleRequest")

    node = Node.get_by_machine_id(request.machine_id)
    if not node:
        return not_ok(
            Error(code=ErrorCode.UNABLE_TO_FIND, errors=["unable to find node"]),
            context=request.machine_id,
        )

    allowed = True
    work_stopped = False

    if not node.can_process_new_work():
        allowed = False

    task = Task.get_by_task_id(request.task_id)

    work_stopped = isinstance(task, Error) or task.state in TaskState.shutting_down()
    if work_stopped:
        allowed = False

    return ok(CanSchedule(allowed=allowed, work_stopped=work_stopped))


def main(req: func.HttpRequest) -> func.HttpResponse:
    if req.method == "POST":
        m = post
    else:
        raise Exception("invalid method")

    return verify_token(req, m)
