#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.models import Error, NodeCommandEnvelope
from onefuzztypes.requests import NodeCommandDelete, NodeCommandGet
from onefuzztypes.responses import BoolResult, PendingNodeCommand

from ..onefuzzlib.endpoint_authorization import call_if_agent
from ..onefuzzlib.request import not_ok, ok, parse_request
from ..onefuzzlib.workers.nodes import NodeMessage


def get(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(NodeCommandGet, req)

    if isinstance(request, Error):
        return not_ok(request, context="NodeCommandGet")

    messages = NodeMessage.get_messages(request.machine_id, num_results=1)

    if messages:
        command = messages[0].message
        message_id = messages[0].message_id
        envelope = NodeCommandEnvelope(command=command, message_id=message_id)

        return ok(PendingNodeCommand(envelope=envelope))
    else:
        return ok(PendingNodeCommand(envelope=None))


def delete(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(NodeCommandDelete, req)
    if isinstance(request, Error):
        return not_ok(request, context="NodeCommandDelete")

    message = NodeMessage.get(request.machine_id, request.message_id)
    if message:
        message.delete()
    return ok(BoolResult(result=True))


def main(req: func.HttpRequest) -> func.HttpResponse:
    methods = {"DELETE": delete, "GET": get}
    method = methods[req.method]
    result = call_if_agent(req, method)

    return result
