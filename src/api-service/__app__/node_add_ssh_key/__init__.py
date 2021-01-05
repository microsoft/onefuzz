#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.requests import NodeAddSshKey
from onefuzztypes.responses import BoolResult

from ..onefuzzlib.pools import Node
from ..onefuzzlib.request import not_ok, ok, parse_request


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
    if req.method == "POST":
        return post(req)
    else:
        raise Exception("invalid method")
