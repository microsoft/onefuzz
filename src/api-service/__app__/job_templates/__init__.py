#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.job_templates import JobTemplateRequest
from onefuzztypes.models import Error

from ..onefuzzlib.job_templates.templates import JobTemplateIndex
from ..onefuzzlib.request import not_ok, ok, parse_request
from ..onefuzzlib.user_credentials import parse_jwt_token


def get(req: func.HttpRequest) -> func.HttpResponse:
    configs = JobTemplateIndex.get_configs()
    return ok(configs)


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(JobTemplateRequest, req)
    if isinstance(request, Error):
        return not_ok(request, context="JobTemplateRequest")

    user_info = parse_jwt_token(req)
    if isinstance(user_info, Error):
        return not_ok(user_info, context="JobTemplateRequest")

    job = JobTemplateIndex.execute(request, user_info)
    if isinstance(job, Error):
        return not_ok(job, context="JobTemplateRequest")

    return ok(job)


def main(req: func.HttpRequest) -> func.HttpResponse:
    if req.method == "GET":
        return get(req)
    elif req.method == "POST":
        return post(req)
    else:
        raise Exception("invalid method")
