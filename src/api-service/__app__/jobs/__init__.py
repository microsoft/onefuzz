#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.enums import ContainerType, ErrorCode, JobState
from onefuzztypes.models import Error, JobConfig, JobTaskInfo
from onefuzztypes.primitives import Container
from onefuzztypes.requests import JobGet, JobSearch

from ..onefuzzlib.azure.containers import create_container
from ..onefuzzlib.azure.storage import StorageType
from ..onefuzzlib.endpoint_authorization import call_if_user
from ..onefuzzlib.jobs import Job
from ..onefuzzlib.request import not_ok, ok, parse_request
from ..onefuzzlib.tasks.main import Task
from ..onefuzzlib.user_credentials import parse_jwt_token


def get(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(JobSearch, req)
    if isinstance(request, Error):
        return not_ok(request, context="jobs")

    if request.job_id:
        job = Job.get(request.job_id)
        if not job:
            return not_ok(
                Error(code=ErrorCode.INVALID_JOB, errors=["no such job"]),
                context=request.job_id,
            )
        task_info = []
        for task in Task.search_states(job_id=request.job_id):
            task_info.append(
                JobTaskInfo(
                    task_id=task.task_id, type=task.config.task.type, state=task.state
                )
            )
        job.task_info = task_info
        return ok(job)

    jobs = Job.search_states(states=request.state)
    return ok(jobs)


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(JobConfig, req)
    if isinstance(request, Error):
        return not_ok(request, context="jobs create")

    user_info = parse_jwt_token(req)
    if isinstance(user_info, Error):
        return not_ok(user_info, context="jobs create")

    job = Job(config=request, user_info=user_info)
    job.save()

    # create the job logs container
    log_container_sas = create_container(
        Container(f"logs-{job.job_id}"),
        StorageType.corpus,
        metadata={"container_type": ContainerType.logs.name},
    )
    if not log_container_sas:
        return not_ok(
            Error(
                code=ErrorCode.UNABLE_TO_CREATE_CONTAINER,
                errors=["unable to create logs container"],
            ),
            context="logs",
        )
    sep_index = log_container_sas.find("?")
    if sep_index > 0:
        log_container = log_container_sas[:sep_index]
    else:
        log_container = log_container_sas

    job.config.logs = log_container
    job.save()

    return ok(job)


def delete(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(JobGet, req)
    if isinstance(request, Error):
        return not_ok(request, context="jobs delete")

    job = Job.get(request.job_id)
    if not job:
        return not_ok(
            Error(code=ErrorCode.INVALID_JOB, errors=["no such job"]),
            context=request.job_id,
        )

    if job.state not in [JobState.stopped, JobState.stopping]:
        job.state = JobState.stopping
        job.save()

    return ok(job)


def main(req: func.HttpRequest) -> func.HttpResponse:
    methods = {"GET": get, "POST": post, "DELETE": delete}
    method = methods[req.method]
    result = call_if_user(req, method)

    return result
