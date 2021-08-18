#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.enums import ErrorCode
from onefuzztypes.job_templates import (
    JobTemplateDelete,
    JobTemplateGet,
    JobTemplateUpload,
)
from onefuzztypes.models import Error
from onefuzztypes.responses import BoolResult

from ..onefuzzlib.endpoint_authorization import call_if_user
from ..onefuzzlib.job_templates.templates import JobTemplateIndex
from ..onefuzzlib.request import not_ok, ok, parse_request


def get(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(JobTemplateGet, req)
    if isinstance(request, Error):
        return not_ok(request, context="JobTemplateGet")

    if request.name:
        entry = JobTemplateIndex.get_base_entry(request.name)
        if entry is None:
            return not_ok(
                Error(code=ErrorCode.INVALID_REQUEST, errors=["no such job template"]),
                context="JobTemplateGet",
            )
        return ok(entry.template)

    templates = JobTemplateIndex.get_index()
    return ok(templates)


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(JobTemplateUpload, req)
    if isinstance(request, Error):
        return not_ok(request, context="JobTemplateUpload")

    entry = JobTemplateIndex(name=request.name, template=request.template)
    result = entry.save()
    if isinstance(result, Error):
        return not_ok(result, context="JobTemplateUpload")

    return ok(BoolResult(result=True))


def delete(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(JobTemplateDelete, req)
    if isinstance(request, Error):
        return not_ok(request, context="JobTemplateDelete")

    entry = JobTemplateIndex.get(request.name)
    if entry is not None:
        entry.delete()

    return ok(BoolResult(result=entry is not None))


def main(req: func.HttpRequest) -> func.HttpResponse:
    methods = {"GET": get, "POST": post, "DELETE": delete}
    method = methods[req.method]
    result = call_if_user(req, method)

    return result
