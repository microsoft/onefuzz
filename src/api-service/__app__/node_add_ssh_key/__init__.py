#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.requests import NodeAddSshKey
from onefuzztypes.responses import BoolResult

from ..onefuzzlib.endpoint_authorization import call_if_user
from ..onefuzzlib.request import not_ok, ok, parse_request
from ..onefuzzlib.workers.nodes import Node


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(NodeAddSshKey, req)
    if isinstance(request, Error):
        return not_ok(request, context="NodeAddSshKey")

    node = Node.get_by_machine_id(request.machine_id)
    if not node:
        return not_ok(
            Error(code=ErrorCode.UNABLE_TO_FIND, errors=["unable to find node"]),
            context=request.machine_id,
        )
    result = node.add_ssh_public_key(public_key=request.public_key)
    if isinstance(result, Error):
        return not_ok(result, context="NodeAddSshKey")

    return ok(BoolResult(result=True))


def main(req: func.HttpRequest) -> func.HttpResponse:
    methods = {"POST": post}
    method = methods[req.method]
    result = call_if_user(req, method)

    return result
