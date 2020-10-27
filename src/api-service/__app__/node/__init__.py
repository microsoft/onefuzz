#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import azure.functions as func
from onefuzztypes.enums import ErrorCode
from onefuzztypes.models import Error
from onefuzztypes.requests import NodeGet, NodeSearch, NodeUpdate
from onefuzztypes.responses import BoolResult

from ..onefuzzlib.pools import Node, NodeTasks
from ..onefuzzlib.request import not_ok, ok, parse_request


def get(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(NodeSearch, req)
    if isinstance(request, Error):
        return not_ok(request, context="pool get")

    if request.machine_id:
        node = Node.get_by_machine_id(request.machine_id)
        if not node:
            return not_ok(
                Error(code=ErrorCode.UNABLE_TO_FIND, errors=["unable to find node"]),
                context=request.machine_id,
            )

        if isinstance(node, Error):
            return not_ok(node, context=request.machine_id)

        node_tasks = NodeTasks.get_by_machine_id(request.machine_id)
        node.tasks = [(t.task_id, t.state) for t in node_tasks]

        return ok(node)

    nodes = Node.search_states(
        states=request.state,
        pool_name=request.pool_name,
        scaleset_id=request.scaleset_id,
    )
    return ok(nodes)


def post(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(NodeUpdate, req)
    if isinstance(request, Error):
        return not_ok(request, context="NodeUpdate")

    node = Node.get_by_machine_id(request.machine_id)
    if not node:
        return not_ok(
            Error(code=ErrorCode.UNABLE_TO_FIND, errors=["unable to find node"]),
            context=request.machine_id,
        )
    if request.debug_keep_node is not None:
        node.debug_keep_node = request.debug_keep_node

    node.save()
    return ok(BoolResult(result=True))


def delete(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(NodeGet, req)
    if isinstance(request, Error):
        return not_ok(request, context="NodeDelete")

    node = Node.get_by_machine_id(request.machine_id)
    if not node:
        return not_ok(
            Error(code=ErrorCode.UNABLE_TO_FIND, errors=["unable to find node"]),
            context=request.machine_id,
        )

    node.set_halt()
    if node.debug_keep_node:
        node.debug_keep_node = False
        node.save()

    return ok(BoolResult(result=True))


def patch(req: func.HttpRequest) -> func.HttpResponse:
    request = parse_request(NodeGet, req)
    if isinstance(request, Error):
        return not_ok(request, context="NodeRestart")

    node = Node.get_by_machine_id(request.machine_id)
    if not node:
        return not_ok(
            Error(code=ErrorCode.UNABLE_TO_FIND, errors=["unable to find node"]),
            context=request.machine_id,
        )

    node.stop()
    if node.debug_keep_node:
        node.debug_keep_node = False
        node.save()
    return ok(BoolResult(result=True))


def main(req: func.HttpRequest) -> func.HttpResponse:
    if req.method == "GET":
        return get(req)
    elif req.method == "DELETE":
        return delete(req)
    elif req.method == "PATCH":
        return patch(req)
    else:
        raise Exception("invalid method")
