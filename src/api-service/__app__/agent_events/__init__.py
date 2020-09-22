#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from uuid import UUID

import azure.functions as func
from onefuzztypes.enums import ErrorCode, NodeState, NodeTaskState, TaskState
from onefuzztypes.models import (
    Error,
    NodeDoneEvent,
    NodeEventEnvelope,
    NodeStateUpdate,
    WorkerDoneEvent,
    WorkerEvent,
    WorkerRunningEvent,
)
from onefuzztypes.responses import BoolResult

from ..onefuzzlib.agent_authorization import verify_token
from ..onefuzzlib.pools import Node, NodeTasks
from ..onefuzzlib.request import RequestException, not_ok, ok, parse_request
from ..onefuzzlib.task_event import TaskEvent
from ..onefuzzlib.tasks.main import Task

ERROR_CONTEXT = "node event"


def get_task_checked(task_id: UUID) -> Task:
    task = Task.get_by_task_id(task_id)
    if isinstance(task, Error):
        raise RequestException(task)
    return task


def get_node_checked(machine_id: UUID) -> Node:
    node = Node.get_by_machine_id(machine_id)
    if not node:
        err = Error(code=ErrorCode.INVALID_NODE, errors=["unable to find node"])
        raise RequestException(err)
    return node


def on_node_done_event(machine_id: UUID, state: NodeState) -> func.HttpResponse:
    node = get_node_checked(machine_id)

    if state != NodeState.done:
        err = Error(
            code=ErrorCode.INVALID_REQUEST,
            errors=["invalid node state for event"],
        )
        return not_ok(err, context=ERROR_CONTEXT)

    # TODO: ensure tasks will be failed.

    node.state = state
    node.save()

    return ok(BoolResult(result=True))


def on_state_update(machine_id: UUID, state: NodeState) -> func.HttpResponse:
    node = get_node_checked(machine_id)

    if state == NodeState.init or node.state not in NodeState.ready_for_reset():
        if node.state != state:
            node.state = state
            node.save()
    else:
        logging.info("ignoring state updates from the node: %s: %s", machine_id, state)

    return ok(BoolResult(result=True))


def on_worker_event(machine_id: UUID, worker_event: WorkerEvent) -> func.HttpResponse:
    task_id = worker_event.event.task_id
    task = get_task_checked(task_id)
    node = get_node_checked(machine_id)
    node_task = NodeTasks(
        machine_id=machine_id, task_id=task_id, state=NodeTaskState.running
    )

    if isinstance(worker_event.event, WorkerRunningEvent):
        if task.state not in TaskState.shutting_down():
            task.state = TaskState.running
        if node.state not in NodeState.ready_for_reset():
            node.state = NodeState.busy
        node_task.save()
        task.on_start()
    elif isinstance(worker_event.event, WorkerDoneEvent):
        # only record exit status if the task isn't already shutting down.
        #
        # the agent failing because resources vanish out from underneath it during
        # deletion is OK
        if task.state not in TaskState.shutting_down():
            exit_status = worker_event.event.exit_status
            if not exit_status.success:
                logging.error("task failed: status = %s", exit_status)

                task.error = Error(
                    code=ErrorCode.TASK_FAILED,
                    errors=[
                        "task failed. exit_status = %s" % exit_status,
                        worker_event.event.stdout,
                        worker_event.event.stderr,
                    ],
                )

            task.state = TaskState.stopping
        if node.state not in NodeState.ready_for_reset():
            node.state = NodeState.done
        node_task.delete()
    else:
        err = Error(
            code=ErrorCode.INVALID_REQUEST,
            errors=["invalid worker event type"],
        )
        raise RequestException(err)

    task.save()
    node.save()
    task_event = TaskEvent(
        task_id=task_id, machine_id=machine_id, event_data=worker_event
    )
    task_event.save()
    return ok(BoolResult(result=True))


def post(req: func.HttpRequest) -> func.HttpResponse:
    envelope = parse_request(NodeEventEnvelope, req)
    if isinstance(envelope, Error):
        return not_ok(envelope, context=ERROR_CONTEXT)

    logging.info(
        "node event: machine_id: %s event: %s",
        envelope.machine_id,
        envelope.event,
    )

    if isinstance(envelope.event, NodeDoneEvent):
        return on_node_done_event(envelope.machine_id, envelope.event.state)
    elif isinstance(envelope.event, NodeStateUpdate):
        return on_state_update(envelope.machine_id, envelope.event.state)
    elif isinstance(envelope.event, WorkerEvent):
        return on_worker_event(envelope.machine_id, envelope.event)
    else:
        err = Error(code=ErrorCode.INVALID_REQUEST, errors=["invalid node event"])
        return not_ok(err, context=ERROR_CONTEXT)


def main(req: func.HttpRequest) -> func.HttpResponse:
    try:
        if req.method == "POST":
            m = post
        else:
            raise Exception("invalid method")

        return verify_token(req, m)
    except RequestException as r:
        return not_ok(r.error, context=ERROR_CONTEXT)
