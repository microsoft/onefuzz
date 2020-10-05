#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Optional, cast
from uuid import UUID

import azure.functions as func
from onefuzztypes.enums import ErrorCode, NodeState, NodeTaskState, TaskState
from onefuzztypes.models import (
    Error,
    NodeDoneEventData
    NodeEvent,
    NodeEventEnvelope,
    NodeSettingUpEventData,
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
) -> None:
    state = state_update.state
    node = get_node_checked(machine_id)

    if state == NodeState.free:
        if node.reimage_requested or node.delete_requested:
            logging.info("stopping free node with reset flags: %s", node.machine_id)
            node.stop()
            return

        if node.could_shrink_scaleset():
            logging.info("stopping free node to resize scaleset: %s", node.machine_id)
            node.set_halt()
            return

    if state == NodeState.init:
        if node.delete_requested:
            node.stop()
            return
        node.reimage_requested = False
        node.save()
    elif node.state not in NodeState.ready_for_reset():
        if node.state != state:
            node.state = state
            node.save()

            if state == NodeState.setting_up:
                # Model-validated.
                #
                # This field will be required in the future.
                # For now, it is optional for back compat.
                setting_up_data = cast(
                    Optional[NodeSettingUpEventData],
                    state_update.data,
                )

                if setting_up_data:
                    for task_id in setting_up_data.tasks:
                        task = get_task_checked(task_id)

                        # The task state may be `running` if it has `vm_count` > 1, and
                        # another node is concurrently executing the task. If so, leave
                        # the state as-is, to represent the max progress made.
                        #
                        # Other states we would want to preserve are excluded by the
                        # outermost conditional check.
                        if task.state != TaskState.running:
                            task.state = TaskState.setting_up

                        task.on_start()
                        task.save()

                        # Note: we set the node task state to `setting_up`, even though
                        # the task itself may be `running`.
                        node_task = NodeTasks(
                            machine_id=machine_id,
                            task_id=task_id,
                            state=NodeTaskState.setting_up,
                        )
                        node_task.save()

            elif state == NodeState.done:
                # if tasks are running on the node when it reports as Done
                # those are stopped early
                node.mark_tasks_stopped_early()

                # Model-validated.
                #
                # This field will be required in the future.
                # For now, it is optional for back compat.
                done_data = cast(Optional[NodeDoneEventData], state_update.data)
                if done_data:
                    # TODO: do something with this done data
                    if done_data.error:
                        logging.error(
                            "node 'done' with error: machine_id:%s, data:%s",
                            machine_id,
                            done_data,
                        )
        else:
            logging.info("No change in Node state")
    else:
        logging.info("ignoring state updates from the node: %s: %s", machine_id, state)


def on_worker_event(machine_id: UUID, event: WorkerEvent) -> None:
    if event.running:
        task_id = event.running.task_id
    elif event.done:
        task_id = event.done.task_id
    else:
        raise NotImplementedError

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
            node.save()
        node_task.save()

        # Start the clock for the task if it wasn't started already
        # (as happens in 1.0.0 agents)
        task.on_start()
    elif event.done:
        node_task.delete()

        exit_status = event.done.exit_status
        if not exit_status.success:
            logging.error("task failed. status:%s", exit_status)
            task.mark_failed(
                Error(
                    code=ErrorCode.TASK_FAILED,
                    errors=[
                        "task failed. exit_status:%s" % exit_status,
                        event.done.stdout,
                        event.done.stderr,
                    ],
                )
            )
        else:
            task.mark_stopping()

        node.to_reimage(done=True)
    else:
        err = Error(
            code=ErrorCode.INVALID_REQUEST,
            errors=["invalid worker event type"],
        )
        raise RequestException(err)

    task.save()

    task_event = TaskEvent(task_id=task_id, machine_id=machine_id, event_data=event)
    task_event.save()


def post(req: func.HttpRequest) -> func.HttpResponse:
    envelope = parse_request(NodeEventEnvelope, req)
    logging.info(f"request: {req.get_json()}")
    logging.info(f"envelope: {envelope}")
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
        on_state_update(envelope.machine_id, event.state_update)
        return ok(BoolResult(result=True))
    elif event.worker_event:
        on_worker_event(envelope.machine_id, event.worker_event)
        return ok(BoolResult(result=True))
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
