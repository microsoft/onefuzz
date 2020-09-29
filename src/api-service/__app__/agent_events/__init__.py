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
    NodeEvent,
    NodeEventEnvelope,
    NodeStateUpdate,
    WorkerEvent,
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


def on_state_update(
    machine_id: UUID,
    state_update: NodeStateUpdate,
) -> func.HttpResponse:
    state = state_update.state
    node = get_node_checked(machine_id)

    if state == NodeState.init or node.state not in NodeState.ready_for_reset():
        if node.state != state:
            node.state = state
            node.save()

            if state == NodeState.setting_up:
                # This field will be required in the future.
                # For now, it is optional for back compat.
                if state_update.data:
                    for task_id in state_update.data.tasks:
                        task = get_task_checked(task_id)
                        task.state = TaskState.setting_up

                        # We don't yet call `on_start()` for the task.
                        # This will happen once we see a worker event that
                        # reports it as `running`.
                        task.save()

                        node_task = NodeTasks(
                            machine_id=machine_id,
                            task_id=task_id,
                            state=NodeTaskState.setting_up,
                        )
                        node_task.save()
    else:
        logging.info("ignoring state updates from the node: %s: %s", machine_id, state)

    return ok(BoolResult(result=True))


def on_worker_event(machine_id: UUID, event: WorkerEvent) -> func.HttpResponse:
    if event.running:
        task_id = event.running.task_id
    elif event.done:
        task_id = event.done.task_id

    task = get_task_checked(task_id)
    node = get_node_checked(machine_id)
    node_task = NodeTasks(
        machine_id=machine_id, task_id=task_id, state=NodeTaskState.running
    )

    if event.running:
        if task.state not in TaskState.shutting_down():
            task.state = TaskState.running
        if node.state not in NodeState.ready_for_reset():
            node.state = NodeState.busy
        node_task.save()
        task.on_start()
    elif event.done:
        # Only record exit status if the task isn't already shutting down.
        #
        # It's ok for the agent to fail because resources vanish out from underneath
        # it during deletion.
        if task.state not in TaskState.shutting_down():
            exit_status = event.done.exit_status

            if not exit_status.success:
                logging.error("task failed: status = %s", exit_status)

                task.error = Error(
                    code=ErrorCode.TASK_FAILED,
                    errors=[
                        "task failed. exit_status = %s" % exit_status,
                        event.done.stdout,
                        event.done.stderr,
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
    task_event = TaskEvent(task_id=task_id, machine_id=machine_id, event_data=event)
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

    if isinstance(envelope.event, NodeEvent):
        event = envelope.event
    elif isinstance(envelope.event, NodeStateUpdate):
        event = NodeEvent(state_update=envelope.event)
    elif isinstance(envelope.event, WorkerEvent):
        event = NodeEvent(worker_event=envelope.event)
    else:
        err = Error(code=ErrorCode.INVALID_REQUEST, errors=["invalid node event"])
        return not_ok(err, context=ERROR_CONTEXT)

    if event.state_update:
        return on_state_update(envelope.machine_id, event.state_update)
    elif event.worker_event:
        return on_worker_event(envelope.machine_id, event.worker_event)
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
