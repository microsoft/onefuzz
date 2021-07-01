#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import logging
from typing import List, Optional, Tuple
from uuid import UUID

from onefuzztypes.enums import ErrorCode, NodeState, PoolState, ScalesetState, TaskState
from onefuzztypes.events import (
    EventNodeCreated,
    EventNodeDeleted,
    EventNodeStateUpdated,
)
from onefuzztypes.models import Error
from onefuzztypes.models import Node as BASE_NODE
from onefuzztypes.models import (
    NodeAssignment,
    NodeCommand,
    NodeCommandAddSshKey,
    NodeCommandStopIfFree,
)
from onefuzztypes.models import NodeTasks as BASE_NODE_TASK
from onefuzztypes.models import Result, StopNodeCommand, StopTaskNodeCommand
from onefuzztypes.primitives import PoolName
from pydantic import Field

from ..__version__ import __version__
from ..azure.vmss import get_instance_id
from ..events import send_event
from ..orm import MappingIntStrAny, ORMMixin, QueryFilter
from ..versions import is_minimum_version

NODE_EXPIRATION_TIME: datetime.timedelta = datetime.timedelta(hours=1)
NODE_REIMAGE_TIME: datetime.timedelta = datetime.timedelta(days=7)

# Future work:
#
# Enabling autoscaling for the scalesets based on the pool work queues.
# https://docs.microsoft.com/en-us/azure/azure-monitor/platform/autoscale-common-metrics#commonly-used-storage-metrics


class Node(BASE_NODE, ORMMixin):
    @classmethod
    def create(
        cls,
        *,
        pool_name: PoolName,
        machine_id: UUID,
        scaleset_id: Optional[UUID],
        version: str,
        new: bool = False,
    ) -> "Node":
        node = cls(
            pool_name=pool_name,
            machine_id=machine_id,
            scaleset_id=scaleset_id,
            version=version,
        )
        # `save` returns None if it's successfully saved.  If `new` is set to
        # True, `save` returns an Error if an object already exists.  As such,
        # only send an event if result is None
        result = node.save(new=new)
        if result is None:
            send_event(
                EventNodeCreated(
                    machine_id=node.machine_id,
                    scaleset_id=node.scaleset_id,
                    pool_name=node.pool_name,
                )
            )
        return node

    @classmethod
    def search_states(
        cls,
        *,
        scaleset_id: Optional[UUID] = None,
        states: Optional[List[NodeState]] = None,
        pool_name: Optional[PoolName] = None,
    ) -> List["Node"]:
        query: QueryFilter = {}
        if scaleset_id:
            query["scaleset_id"] = [scaleset_id]
        if states:
            query["state"] = states
        if pool_name:
            query["pool_name"] = [pool_name]
        return cls.search(query=query)

    @classmethod
    def search_outdated(
        cls,
        *,
        scaleset_id: Optional[UUID] = None,
        states: Optional[List[NodeState]] = None,
        pool_name: Optional[PoolName] = None,
        exclude_update_scheduled: bool = False,
        num_results: Optional[int] = None,
    ) -> List["Node"]:
        query: QueryFilter = {}
        if scaleset_id:
            query["scaleset_id"] = [scaleset_id]
        if states:
            query["state"] = states
        if pool_name:
            query["pool_name"] = [pool_name]

        if exclude_update_scheduled:
            query["reimage_requested"] = [False]
            query["delete_requested"] = [False]

        # azure table query always return false when the column does not exist
        # We write the query this way to allow us to get the nodes where the
        # version is not defined as well as the nodes with a mismatched version
        version_query = "not (version eq '%s')" % __version__
        return cls.search(
            query=query, raw_unchecked_filter=version_query, num_results=num_results
        )

    @classmethod
    def mark_outdated_nodes(cls) -> None:
        # ony update 500 nodes at a time to mitigate timeout issues
        outdated = cls.search_outdated(exclude_update_scheduled=True, num_results=500)
        for node in outdated:
            logging.info(
                "node is outdated: %s - node_version:%s api_version:%s",
                node.machine_id,
                node.version,
                __version__,
            )
            if node.version == "1.0.0":
                node.to_reimage(done=True)
            else:
                node.to_reimage()

    @classmethod
    def cleanup_busy_nodes_without_work(cls) -> None:
        # There is a potential race condition if multiple `Node.stop_task` calls
        # are made concurrently.  By performing this check regularly, any nodes
        # that hit this race condition will get cleaned up.
        for node in cls.search_states(states=[NodeState.busy]):
            node.stop_if_complete()

    @classmethod
    def get_by_machine_id(cls, machine_id: UUID) -> Optional["Node"]:
        nodes = cls.search(query={"machine_id": [machine_id]})
        if not nodes:
            return None

        if len(nodes) != 1:
            return None
        return nodes[0]

    @classmethod
    def key_fields(cls) -> Tuple[str, str]:
        return ("pool_name", "machine_id")

    def save_exclude(self) -> Optional[MappingIntStrAny]:
        return {"tasks": ..., "messages": ...}

    def telemetry_include(self) -> Optional[MappingIntStrAny]:
        return {
            "machine_id": ...,
            "state": ...,
            "scaleset_id": ...,
        }

    def scaleset_node_exists(self) -> bool:
        if self.scaleset_id is None:
            return False

        from .scalesets import Scaleset

        scaleset = Scaleset.get_by_id(self.scaleset_id)
        if not isinstance(scaleset, Scaleset):
            return False

        instance_id = get_instance_id(scaleset.scaleset_id, self.machine_id)
        return isinstance(instance_id, str)

    @classmethod
    def stop_task(cls, task_id: UUID) -> None:
        # For now, this just re-images the node.  Eventually, this
        # should send a message to the node to let the agent shut down
        # gracefully
        nodes = NodeTasks.get_nodes_by_task_id(task_id)
        for node in nodes:
            node.send_message(
                NodeCommand(stop_task=StopTaskNodeCommand(task_id=task_id))
            )

            if not node.stop_if_complete():
                logging.info(
                    "nodes: stopped task on node, "
                    "but not reimaging due to other tasks: task_id:%s machine_id:%s",
                    task_id,
                    node.machine_id,
                )

    def stop_if_complete(self) -> bool:
        # returns True on stopping the node and False if this doesn't stop the node
        from ..tasks.main import Task

        node_tasks = NodeTasks.get_by_machine_id(self.machine_id)
        for node_task in node_tasks:
            task = Task.get_by_task_id(node_task.task_id)
            # ignore invalid tasks when deciding if the node should be
            # shutdown
            if isinstance(task, Error):
                continue

            if task.state not in TaskState.shutting_down():
                return False

        logging.info(
            "node: stopping busy node with all tasks complete: %s",
            self.machine_id,
        )
        self.stop(done=True)
        return True

    def mark_tasks_stopped_early(self, error: Optional[Error] = None) -> None:
        from ..tasks.main import Task

        if error is None:
            error = Error(
                code=ErrorCode.TASK_FAILED,
                errors=[
                    "node reimaged during task execution.  machine_id:%s"
                    % self.machine_id
                ],
            )

        for entry in NodeTasks.get_by_machine_id(self.machine_id):
            task = Task.get_by_task_id(entry.task_id)
            if isinstance(task, Task):
                task.mark_failed(error)
            if not self.debug_keep_node:
                entry.delete()

    def could_shrink_scaleset(self) -> bool:
        from .scalesets import ScalesetShrinkQueue

        if self.scaleset_id and ScalesetShrinkQueue(self.scaleset_id).should_shrink():
            return True
        return False

    def can_process_new_work(self) -> bool:
        from .pools import Pool
        from .scalesets import Scaleset

        if self.is_outdated():
            logging.info(
                "can_process_new_work agent and service versions differ, "
                "stopping node. "
                "machine_id:%s agent_version:%s service_version: %s",
                self.machine_id,
                self.version,
                __version__,
            )
            self.stop(done=True)
            return False

        if self.is_too_old():
            logging.info(
                "can_process_new_work node is too old.  machine_id:%s", self.machine_id
            )
            self.stop(done=True)
            return False

        if self.state not in NodeState.can_process_new_work():
            logging.info(
                "can_process_new_work node not in appropriate state for new work"
                "machine_id:%s state:%S",
                self.machine_id,
                self.state.name,
            )
            return False

        if self.state in NodeState.ready_for_reset():
            logging.info(
                "can_process_new_work node is set for reset.  machine_id:%s",
                self.machine_id,
            )
            return False

        if self.delete_requested:
            logging.info(
                "can_process_new_work is set to be deleted.  machine_id:%s",
                self.machine_id,
            )
            self.stop(done=True)
            return False

        if self.reimage_requested:
            logging.info(
                "can_process_new_work is set to be reimaged.  machine_id:%s",
                self.machine_id,
            )
            self.stop(done=True)
            return False

        if self.could_shrink_scaleset():
            logging.info(
                "can_process_new_work node scheduled to shrink.  machine_id:%s",
                self.machine_id,
            )
            self.set_halt()
            return False

        if self.scaleset_id:
            scaleset = Scaleset.get_by_id(self.scaleset_id)
            if isinstance(scaleset, Error):
                logging.info(
                    "can_process_new_work invalid scaleset.  "
                    "scaleset_id:%s machine_id:%s",
                    self.scaleset_id,
                    self.machine_id,
                )
                return False

            if scaleset.state not in ScalesetState.available():
                logging.info(
                    "can_process_new_work scaleset not available for work. "
                    "scaleset_id:%s machine_id:%s",
                    self.scaleset_id,
                    self.machine_id,
                )
                return False

        pool = Pool.get_by_name(self.pool_name)
        if isinstance(pool, Error):
            logging.info(
                "can_schedule - invalid pool. " "pool_name:%s machine_id:%s",
                self.pool_name,
                self.machine_id,
            )
            return False
        if pool.state not in PoolState.available():
            logging.info(
                "can_schedule - pool is not available for work. "
                "pool_name:%s machine_id:%s",
                self.pool_name,
                self.machine_id,
            )
            return False

        return True

    def is_outdated(self) -> bool:
        return self.version != __version__

    def is_too_old(self) -> bool:
        return (
            self.scaleset_id is not None
            and self.timestamp is not None
            and self.timestamp
            < datetime.datetime.now(datetime.timezone.utc) - NODE_REIMAGE_TIME
        )

    def send_message(self, message: NodeCommand) -> None:
        NodeMessage(
            machine_id=self.machine_id,
            message=message,
        ).save()

    def to_reimage(self, done: bool = False) -> None:
        if done:
            if self.state not in NodeState.ready_for_reset():
                self.state = NodeState.done

        if not self.reimage_requested and not self.delete_requested:
            logging.info("setting reimage_requested: %s", self.machine_id)
            self.reimage_requested = True

        # if we're going to reimage, make sure the node doesn't pick up new work
        # too.
        self.send_stop_if_free()

        self.save()

    def add_ssh_public_key(self, public_key: str) -> Result[None]:
        if self.scaleset_id is None:
            return Error(
                code=ErrorCode.INVALID_REQUEST,
                errors=["only able to add ssh keys to scaleset nodes"],
            )

        if not public_key.endswith("\n"):
            public_key += "\n"

        self.send_message(
            NodeCommand(add_ssh_key=NodeCommandAddSshKey(public_key=public_key))
        )
        return None

    def send_stop_if_free(self) -> None:
        if is_minimum_version(version=self.version, minimum="2.16.1"):
            self.send_message(NodeCommand(stop_if_free=NodeCommandStopIfFree()))

    def stop(self, done: bool = False) -> None:
        self.to_reimage(done=done)
        self.send_message(NodeCommand(stop=StopNodeCommand()))

    def set_shutdown(self) -> None:
        # don't give out more work to the node, but let it finish existing work
        logging.info("setting delete_requested: %s", self.machine_id)
        self.delete_requested = True
        self.save()
        self.send_stop_if_free()

    def set_halt(self) -> None:
        """Tell the node to stop everything."""
        logging.info("setting halt: %s", self.machine_id)
        self.delete_requested = True
        self.stop(done=True)
        self.set_state(NodeState.halt)

    @classmethod
    def get_dead_nodes(
        cls, scaleset_id: UUID, expiration_period: datetime.timedelta
    ) -> List["Node"]:
        time_filter = "heartbeat lt datetime'%s'" % (
            (datetime.datetime.utcnow() - expiration_period).isoformat()
        )
        return cls.search(
            query={"scaleset_id": [scaleset_id]},
            raw_unchecked_filter=time_filter,
        )

    @classmethod
    def reimage_long_lived_nodes(cls, scaleset_id: UUID) -> None:
        """
        Mark any excessively long lived node to be re-imaged.

        This helps keep nodes on scalesets that use `latest` OS image SKUs
        reasonably up-to-date with OS patches without disrupting running
        fuzzing tasks with patch reboot cycles.
        """
        time_filter = "Timestamp lt datetime'%s'" % (
            (datetime.datetime.utcnow() - NODE_REIMAGE_TIME).isoformat()
        )
        # skip any nodes already marked for reimage/deletion
        for node in cls.search(
            query={
                "scaleset_id": [scaleset_id],
                "reimage_requested": [False],
                "delete_requested": [False],
            },
            raw_unchecked_filter=time_filter,
        ):
            if node.debug_keep_node:
                logging.info(
                    "removing debug_keep_node for expired node. "
                    "scaleset_id:%s machine_id:%s",
                    node.scaleset_id,
                    node.machine_id,
                )
                node.debug_keep_node = False
            node.to_reimage()

    def set_state(self, state: NodeState) -> None:
        if self.state != state:
            self.state = state
            send_event(
                EventNodeStateUpdated(
                    machine_id=self.machine_id,
                    pool_name=self.pool_name,
                    scaleset_id=self.scaleset_id,
                    state=state,
                )
            )

        self.save()

    def delete(self) -> None:
        self.mark_tasks_stopped_early()
        NodeTasks.clear_by_machine_id(self.machine_id)
        NodeMessage.clear_messages(self.machine_id)
        super().delete()
        send_event(
            EventNodeDeleted(
                machine_id=self.machine_id,
                pool_name=self.pool_name,
                scaleset_id=self.scaleset_id,
            )
        )


class NodeTasks(BASE_NODE_TASK, ORMMixin):
    @classmethod
    def key_fields(cls) -> Tuple[str, str]:
        return ("machine_id", "task_id")

    def telemetry_include(self) -> Optional[MappingIntStrAny]:
        return {
            "machine_id": ...,
            "task_id": ...,
            "state": ...,
        }

    @classmethod
    def get_nodes_by_task_id(cls, task_id: UUID) -> List["Node"]:
        result = []
        for entry in cls.search(query={"task_id": [task_id]}):
            node = Node.get_by_machine_id(entry.machine_id)
            if node:
                result.append(node)
        return result

    @classmethod
    def get_node_assignments(cls, task_id: UUID) -> List[NodeAssignment]:
        result = []
        for entry in cls.search(query={"task_id": [task_id]}):
            node = Node.get_by_machine_id(entry.machine_id)
            if node:
                node_assignment = NodeAssignment(
                    node_id=node.machine_id,
                    scaleset_id=node.scaleset_id,
                    state=entry.state,
                )
                result.append(node_assignment)

        return result

    @classmethod
    def get_by_machine_id(cls, machine_id: UUID) -> List["NodeTasks"]:
        return cls.search(query={"machine_id": [machine_id]})

    @classmethod
    def get_by_task_id(cls, task_id: UUID) -> List["NodeTasks"]:
        return cls.search(query={"task_id": [task_id]})

    @classmethod
    def clear_by_machine_id(cls, machine_id: UUID) -> None:
        logging.info("clearing tasks for node: %s", machine_id)
        for entry in cls.get_by_machine_id(machine_id):
            entry.delete()


# this isn't anticipated to be needed by the client, hence it not
# being in onefuzztypes
class NodeMessage(ORMMixin):
    machine_id: UUID
    message_id: str = Field(default_factory=datetime.datetime.utcnow().timestamp)
    message: NodeCommand

    @classmethod
    def key_fields(cls) -> Tuple[str, str]:
        return ("machine_id", "message_id")

    @classmethod
    def get_messages(
        cls, machine_id: UUID, num_results: int = None
    ) -> List["NodeMessage"]:
        entries: List["NodeMessage"] = cls.search(
            query={"machine_id": [machine_id]}, num_results=num_results
        )
        return entries

    @classmethod
    def clear_messages(cls, machine_id: UUID) -> None:
        logging.info("clearing messages for node: %s", machine_id)
        messages = cls.get_messages(machine_id)
        for message in messages:
            message.delete()
