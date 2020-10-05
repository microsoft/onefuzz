#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.models import Error, NodeCommandEnvelope
from onefuzztypes.requests import NodeCommandDelete, NodeCommandGet
from onefuzztypes.responses import BoolResult, PendingNodeCommand

from ..onefuzzlib.agent_authorization import verify_token
from ..onefuzzlib.pools import NodeMessage
from ..onefuzzlib.request import not_ok, ok, parse_request


def get(req: func.HttpRequest) -> func.HttpResponse:
    logging.info(f"request: {req}")
    logging.info(f"request params: {req.params}")
    logging.info(f"request body: {req.get_body()}")
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
    if req.method == "GET":
        m = get
    elif req.method == "DELETE":
        m = delete
    else:
        raise Exception("invalid method")

    return verify_token(req, m)
