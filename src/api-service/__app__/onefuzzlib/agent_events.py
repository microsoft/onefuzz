#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
from typing import Optional, cast
from uuid import UUID

from onefuzztypes.enums import (
    ErrorCode,
    NodeState,
    NodeTaskState,
    TaskDebugFlag,
    TaskState,
)
from onefuzztypes.models import (
    Error,
    NodeDoneEventData,
    NodeSettingUpEventData,
    NodeStateUpdate,
    Result,
    WorkerDoneEvent,
    WorkerEvent,
    WorkerRunningEvent,
)

from .task_event import TaskEvent
from .tasks.main import Task
from .workers.nodes import Node, NodeTasks

MAX_OUTPUT_SIZE = 4096


def get_node(machine_id: UUID) -> Result[Node]:
    node = Node.get_by_machine_id(machine_id)
    if not node:
        return Error(code=ErrorCode.INVALID_NODE, errors=["unable to find node"])
    return node


def on_state_update(
    machine_id: UUID,
    state_update: NodeStateUpdate,
) -> Result[None]:
    state = state_update.state
    node = get_node(machine_id)
    if isinstance(node, Error):
        return node

    if state == NodeState.free:
        if node.reimage_requested or node.delete_requested:
            logging.info("stopping free node with reset flags: %s", node.machine_id)
            node.stop()
            return None

        if node.could_shrink_scaleset():
            logging.info("stopping free node to resize scaleset: %s", node.machine_id)
            node.set_halt()
            return None

    if state == NodeState.init:
        if node.delete_requested:
            logging.info("stopping node (init and delete_requested): %s", machine_id)
            node.stop()
            return None

        # not checking reimage_requested, as nodes only send 'init' state once.  If
        # they send 'init' with reimage_requested, it's because the node was reimaged
        # successfully.
        node.reimage_requested = False
        node.set_state(state)
        return None

    logging.info("node state update: %s from:%s to:%s", machine_id, node.state, state)
    node.set_state(state)

    if state == NodeState.free:
        logging.info("node now available for work: %s", machine_id)
    elif state == NodeState.setting_up:
        # Model-validated.
        #
        # This field will be required in the future.
        # For now, it is optional for back compat.
        setting_up_data = cast(
            Optional[NodeSettingUpEventData],
            state_update.data,
        )

        if setting_up_data:
            if not setting_up_data.tasks:
                return Error(
                    code=ErrorCode.INVALID_REQUEST,
                    errors=["setup without tasks.  machine_id: %s", str(machine_id)],
                )

            for task_id in setting_up_data.tasks:
                task = Task.get_by_task_id(task_id)
                if isinstance(task, Error):
                    return task

                logging.info(
                    "node starting task.  machine_id: %s job_id: %s task_id: %s",
                    machine_id,
                    task.job_id,
                    task.task_id,
                )

                # The task state may be `running` if it has `vm_count` > 1, and
                # another node is concurrently executing the task. If so, leave
                # the state as-is, to represent the max progress made.
                #
                # Other states we would want to preserve are excluded by the
                # outermost conditional check.
                if task.state not in [TaskState.running, TaskState.setting_up]:
                    task.set_state(TaskState.setting_up)

                # Note: we set the node task state to `setting_up`, even though
                # the task itself may be `running`.
                node_task = NodeTasks(
                    machine_id=machine_id,
                    task_id=task_id,
                    state=NodeTaskState.setting_up,
                )
                node_task.save()

    elif state == NodeState.done:
        # Model-validated.
        #
        # This field will be required in the future.
        # For now, it is optional for back compat.
        done_data = cast(Optional[NodeDoneEventData], state_update.data)
        error = None
        if done_data:
            if done_data.error:
                error_text = done_data.json(exclude_none=True)
                error = Error(
                    code=ErrorCode.TASK_FAILED,
                    errors=[error_text],
                )
                logging.error(
                    "node 'done' with error: machine_id:%s, data:%s",
                    machine_id,
                    error_text,
                )

        # if tasks are running on the node when it reports as Done
        # those are stopped early
        node.mark_tasks_stopped_early(error=error)
        node.to_reimage(done=True)

    return None


def on_worker_event_running(
    machine_id: UUID, event: WorkerRunningEvent
) -> Result[None]:
    task = Task.get_by_task_id(event.task_id)
    if isinstance(task, Error):
        return task

    node = get_node(machine_id)
    if isinstance(node, Error):
        return node

    if node.state not in NodeState.ready_for_reset():
        node.set_state(NodeState.busy)

    node_task = NodeTasks(
        machine_id=machine_id, task_id=event.task_id, state=NodeTaskState.running
    )
    node_task.save()

    if task.state in TaskState.shutting_down():
        logging.info(
            "ignoring task start from node.  machine_id:%s %s:%s (state: %s)",
            machine_id,
            task.job_id,
            task.task_id,
            task.state,
        )
        return None

    logging.info(
        "task started on node.  machine_id:%s %s:%s",
        machine_id,
        task.job_id,
        task.task_id,
    )
    task.set_state(TaskState.running)

    task_event = TaskEvent(
        task_id=task.task_id,
        machine_id=machine_id,
        event_data=WorkerEvent(running=event),
    )
    task_event.save()

    return None


def on_worker_event_done(machine_id: UUID, event: WorkerDoneEvent) -> Result[None]:
    task = Task.get_by_task_id(event.task_id)
    if isinstance(task, Error):
        return task

    node = get_node(machine_id)
    if isinstance(node, Error):
        return node

    if event.exit_status.success:
        logging.info(
            "task done. %s:%s status:%s", task.job_id, task.task_id, event.exit_status
        )
        task.mark_stopping()
        if (
            task.config.debug
            and TaskDebugFlag.keep_node_on_completion in task.config.debug
        ):
            node.debug_keep_node = True
            node.save()
    else:
        task.mark_failed(
            Error(
                code=ErrorCode.TASK_FAILED,
                errors=[
                    "task failed. exit_status:%s" % event.exit_status,
                    event.stdout[-MAX_OUTPUT_SIZE:],
                    event.stderr[-MAX_OUTPUT_SIZE:],
                ],
            )
        )

        if task.config.debug and (
            TaskDebugFlag.keep_node_on_failure in task.config.debug
            or TaskDebugFlag.keep_node_on_completion in task.config.debug
        ):
            node.debug_keep_node = True
            node.save()

    if not node.debug_keep_node:
        node_task = NodeTasks.get(machine_id, event.task_id)
        if node_task:
            node_task.delete()

    event.stdout = event.stdout[-MAX_OUTPUT_SIZE:]
    event.stderr = event.stderr[-MAX_OUTPUT_SIZE:]
    task_event = TaskEvent(
        task_id=task.task_id, machine_id=machine_id, event_data=WorkerEvent(done=event)
    )
    task_event.save()
    return None


def on_worker_event(machine_id: UUID, event: WorkerEvent) -> Result[None]:
    if event.running:
        return on_worker_event_running(machine_id, event.running)
    elif event.done:
        return on_worker_event_done(machine_id, event.done)
    else:
        raise NotImplementedError
