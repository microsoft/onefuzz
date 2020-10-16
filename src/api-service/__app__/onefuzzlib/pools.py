#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import logging
from typing import Dict, List, Optional, Tuple, Union
from uuid import UUID, uuid4

from onefuzztypes.enums import (
    OS,
    Architecture,
    ErrorCode,
    NodeState,
    PoolState,
    ScalesetState,
)
from onefuzztypes.models import AutoScaleConfig, Error
from onefuzztypes.models import Node as BASE_NODE
from onefuzztypes.models import NodeAssignment, NodeCommand
from onefuzztypes.models import NodeTasks as BASE_NODE_TASK
from onefuzztypes.models import Pool as BASE_POOL
from onefuzztypes.models import Scaleset as BASE_SCALESET
from onefuzztypes.models import (
    ScalesetNodeState,
    ScalesetSummary,
    StopNodeCommand,
    WorkSet,
    WorkSetSummary,
    WorkUnitSummary,
)
from onefuzztypes.primitives import PoolName, Region
from pydantic import BaseModel, Field

from .__version__ import __version__
from .azure.auth import build_auth
from .azure.creds import get_func_storage, get_fuzz_storage
from .azure.image import get_os
from .azure.network import Network
from .azure.queue import (
    clear_queue,
    create_queue,
    delete_queue,
    peek_queue,
    queue_object,
    remove_first_message,
)
from .azure.vmss import (
    UnableToUpdate,
    create_vmss,
    delete_vmss,
    delete_vmss_nodes,
    get_instance_id,
    get_vmss,
    get_vmss_size,
    list_instance_ids,
    reimage_vmss_nodes,
    resize_vmss,
    update_extensions,
)
from .extension import fuzz_extensions
from .orm import MappingIntStrAny, ORMMixin, QueryFilter

# Future work:
#
# Enabling autoscaling for the scalesets based on the pool work queues.
# https://docs.microsoft.com/en-us/azure/azure-monitor/platform/autoscale-common-metrics#commonly-used-storage-metrics


class Node(BASE_NODE, ORMMixin):
    # should only be set by Scaleset.reimage_nodes
    # should only be unset during agent_registration POST
    reimage_queued: bool = Field(default=False)

    @classmethod
    def search_states(
        cls,
        *,
        scaleset_id: Optional[UUID] = None,
        states: Optional[List[NodeState]] = None,
        pool_name: Optional[str] = None,
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
        pool_name: Optional[str] = None,
    ) -> List["Node"]:
        query: QueryFilter = {}
        if scaleset_id:
            query["scaleset_id"] = [scaleset_id]
        if states:
            query["state"] = states
        if pool_name:
            query["pool_name"] = [pool_name]

        # azure table query always return false when the column does not exist
        # We write the query this way to allow us to get the nodes where the
        # version is not defined as well as the nodes with a mismatched version
        version_query = "not (version eq '%s')" % __version__
        return cls.search(query=query, raw_unchecked_filter=version_query)

    @classmethod
    def mark_outdated_nodes(cls) -> None:
        outdated = cls.search_outdated()
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
        return {"tasks": ...}

    def telemetry_include(self) -> Optional[MappingIntStrAny]:
        return {
            "machine_id": ...,
            "state": ...,
            "scaleset_id": ...,
        }

    def event_include(self) -> Optional[MappingIntStrAny]:
        return {
            "pool_name": ...,
            "machine_id": ...,
            "state": ...,
            "scaleset_id": ...,
        }

    def scaleset_node_exists(self) -> bool:
        if self.scaleset_id is None:
            return False

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
            if node.state not in NodeState.ready_for_reset():
                logging.info(
                    "stopping machine_id:%s running task:%s",
                    node.machine_id,
                    task_id,
                )
                node.stop()

    def mark_tasks_stopped_early(self) -> None:
        from .tasks.main import Task

        for entry in NodeTasks.get_by_machine_id(self.machine_id):
            task = Task.get_by_task_id(entry.task_id)
            if isinstance(task, Task):
                task.mark_failed(
                    Error(
                        code=ErrorCode.TASK_FAILED,
                        errors=["node reimaged during task execution"],
                    )
                )
            entry.delete()

    def could_shrink_scaleset(self) -> bool:
        if self.scaleset_id and ScalesetShrinkQueue(self.scaleset_id).should_shrink():
            return True
        return False

    def can_process_new_work(self) -> bool:
        if self.is_outdated():
            logging.info(
                "can_schedule old version machine_id:%s version:%s",
                self.machine_id,
                self.version,
            )
            self.stop()
            return False

        if self.state in NodeState.ready_for_reset():
            logging.info(
                "can_schedule should be recycled.  machine_id:%s", self.machine_id
            )
            return False

        if self.delete_requested or self.reimage_requested:
            logging.info(
                "can_schedule should be recycled.  machine_id:%s", self.machine_id
            )
            self.stop()
            return False

        if self.could_shrink_scaleset():
            self.set_halt()
            logging.info("node scheduled to shrink.  machine_id:%s", self.machine_id)
            return False

        return True

    def is_outdated(self) -> bool:
        return self.version != __version__

    def send_message(self, message: NodeCommand) -> None:
        stop_message = NodeMessage(
            agent_id=self.machine_id,
            message=message,
        )
        stop_message.save()

    def to_reimage(self, done: bool = False) -> None:
        if done:
            if self.state not in NodeState.ready_for_reset():
                self.state = NodeState.done

        if not self.reimage_requested and not self.delete_requested:
            logging.info("setting reimage_requested: %s", self.machine_id)
            self.reimage_requested = True
        self.save()

    def stop(self) -> None:
        self.to_reimage()
        self.send_message(NodeCommand(stop=StopNodeCommand()))

    def set_shutdown(self) -> None:
        # don't give out more work to the node, but let it finish existing work
        logging.info("setting delete_requested: %s", self.machine_id)
        self.delete_requested = True
        self.save()

    def set_halt(self) -> None:
        """ Tell the node to stop everything. """
        self.set_shutdown()
        self.stop()


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


# this isn't anticipated to be needed by the client, hence it not
# being in onefuzztypes
class NodeMessage(ORMMixin):
    agent_id: UUID
    message_id: str = Field(default_factory=datetime.datetime.utcnow().timestamp)
    message: NodeCommand

    @classmethod
    def key_fields(cls) -> Tuple[str, str]:
        return ("agent_id", "message_id")

    @classmethod
    def get_messages(
        cls, agent_id: UUID, num_results: int = None
    ) -> List["NodeMessage"]:
        entries: List["NodeMessage"] = cls.search(
            query={"agent_id": [agent_id]}, num_results=num_results
        )
        return entries

    @classmethod
    def clear_messages(cls, agent_id: UUID) -> None:
        messages = cls.get_messages(agent_id)
        for message in messages:
            message.delete()


class Pool(BASE_POOL, ORMMixin):
    @classmethod
    def create(
        cls,
        *,
        name: PoolName,
        os: OS,
        arch: Architecture,
        managed: bool,
        client_id: Optional[UUID],
        autoscale: Optional[AutoScaleConfig],
    ) -> "Pool":
        return cls(
            name=name,
            os=os,
            arch=arch,
            managed=managed,
            client_id=client_id,
            config=None,
            autoscale=autoscale,
        )

    def save_exclude(self) -> Optional[MappingIntStrAny]:
        return {
            "nodes": ...,
            "queue": ...,
            "work_queue": ...,
            "config": ...,
            "node_summary": ...,
        }

    def export_exclude(self) -> Optional[MappingIntStrAny]:
        return {
            "etag": ...,
            "timestamp": ...,
        }

    def event_include(self) -> Optional[MappingIntStrAny]:
        return {
            "name": ...,
            "pool_id": ...,
            "os": ...,
            "state": ...,
            "managed": ...,
        }

    def telemetry_include(self) -> Optional[MappingIntStrAny]:
        return {
            "pool_id": ...,
            "os": ...,
            "state": ...,
            "managed": ...,
        }

    def populate_scaleset_summary(self) -> None:
        self.scaleset_summary = [
            ScalesetSummary(scaleset_id=x.scaleset_id, state=x.state)
            for x in Scaleset.search_by_pool(self.name)
        ]

    def populate_work_queue(self) -> None:
        self.work_queue = []

        # Only populate the work queue summaries if the pool is initialized. We
        # can then be sure that the queue is available in the operations below.
        if self.state == PoolState.init:
            return

        worksets = peek_queue(
            self.get_pool_queue(), account_id=get_fuzz_storage(), object_type=WorkSet
        )

        for workset in worksets:
            work_units = [
                WorkUnitSummary(
                    job_id=work_unit.job_id,
                    task_id=work_unit.task_id,
                    task_type=work_unit.task_type,
                )
                for work_unit in workset.work_units
            ]
            self.work_queue.append(WorkSetSummary(work_units=work_units))

    def get_pool_queue(self) -> str:
        return "pool-%s" % self.pool_id.hex

    def init(self) -> None:
        create_queue(self.get_pool_queue(), account_id=get_fuzz_storage())
        self.state = PoolState.running
        self.save()

    def schedule_workset(self, work_set: WorkSet) -> bool:
        # Don't schedule work for pools that can't and won't do work.
        if self.state in [PoolState.shutdown, PoolState.halt]:
            return False

        return queue_object(
            self.get_pool_queue(), work_set, account_id=get_fuzz_storage()
        )

    @classmethod
    def get_by_id(cls, pool_id: UUID) -> Union[Error, "Pool"]:
        pools = cls.search(query={"pool_id": [pool_id]})
        if not pools:
            return Error(code=ErrorCode.INVALID_REQUEST, errors=["unable to find pool"])

        if len(pools) != 1:
            return Error(
                code=ErrorCode.INVALID_REQUEST, errors=["error identifying pool"]
            )
        pool = pools[0]
        return pool

    @classmethod
    def get_by_name(cls, name: PoolName) -> Union[Error, "Pool"]:
        pools = cls.search(query={"name": [name]})
        if not pools:
            return Error(code=ErrorCode.INVALID_REQUEST, errors=["unable to find pool"])

        if len(pools) != 1:
            return Error(
                code=ErrorCode.INVALID_REQUEST, errors=["error identifying pool"]
            )
        pool = pools[0]
        return pool

    @classmethod
    def search_states(cls, *, states: Optional[List[PoolState]] = None) -> List["Pool"]:
        query: QueryFilter = {}
        if states:
            query["state"] = states
        return cls.search(query=query)

    def shutdown(self) -> None:
        """ shutdown allows nodes to finish current work then delete """
        scalesets = Scaleset.search_by_pool(self.name)
        nodes = Node.search(query={"pool_name": [self.name]})
        if not scalesets and not nodes:
            logging.info("pool stopped, deleting: %s", self.name)

            self.state = PoolState.halt
            self.delete()
            return

        for scaleset in scalesets:
            scaleset.state = ScalesetState.shutdown
            scaleset.save()

        for node in nodes:
            node.set_shutdown()

        self.save()

    def halt(self) -> None:
        """ halt the pool immediately """
        scalesets = Scaleset.search_by_pool(self.name)
        nodes = Node.search(query={"pool_name": [self.name]})
        if not scalesets and not nodes:
            delete_queue(self.get_pool_queue(), account_id=get_fuzz_storage())
            logging.info("pool stopped, deleting: %s", self.name)
            self.state = PoolState.halt
            self.delete()
            return

        for scaleset in scalesets:
            scaleset.state = ScalesetState.halt
            scaleset.save()

        for node in nodes:
            node.set_halt()

        self.save()

    @classmethod
    def key_fields(cls) -> Tuple[str, str]:
        return ("name", "pool_id")


class Scaleset(BASE_SCALESET, ORMMixin):
    def save_exclude(self) -> Optional[MappingIntStrAny]:
        return {"nodes": ...}

    def event_include(self) -> Optional[MappingIntStrAny]:
        return {
            "pool_name": ...,
            "scaleset_id": ...,
            "state": ...,
            "os": ...,
            "size": ...,
            "error": ...,
        }

    def telemetry_include(self) -> Optional[MappingIntStrAny]:
        return {
            "scaleset_id": ...,
            "os": ...,
            "vm_sku": ...,
            "size": ...,
            "spot_instances": ...,
        }

    @classmethod
    def create(
        cls,
        *,
        pool_name: PoolName,
        vm_sku: str,
        image: str,
        region: Region,
        size: int,
        spot_instances: bool,
        tags: Dict[str, str],
        client_id: Optional[UUID] = None,
        client_object_id: Optional[UUID] = None,
    ) -> "Scaleset":
        return cls(
            pool_name=pool_name,
            vm_sku=vm_sku,
            image=image,
            region=region,
            size=size,
            spot_instances=spot_instances,
            auth=build_auth(),
            client_id=client_id,
            client_object_id=client_object_id,
            tags=tags,
        )

    @classmethod
    def search_by_pool(cls, pool_name: PoolName) -> List["Scaleset"]:
        return cls.search(query={"pool_name": [pool_name]})

    @classmethod
    def get_by_id(cls, scaleset_id: UUID) -> Union[Error, "Scaleset"]:
        scalesets = cls.search(query={"scaleset_id": [scaleset_id]})
        if not scalesets:
            return Error(
                code=ErrorCode.INVALID_REQUEST, errors=["unable to find scaleset"]
            )

        if len(scalesets) != 1:
            return Error(
                code=ErrorCode.INVALID_REQUEST, errors=["error identifying scaleset"]
            )
        scaleset = scalesets[0]
        return scaleset

    @classmethod
    def get_by_object_id(cls, object_id: UUID) -> List["Scaleset"]:
        return cls.search(query={"client_object_id": [object_id]})

    def init(self) -> None:
        logging.info("scaleset init: %s", self.scaleset_id)

        ScalesetShrinkQueue(self.scaleset_id).create()

        # Handle the race condition between a pool being deleted and a
        # scaleset being added to the pool.
        pool = Pool.get_by_name(self.pool_name)
        if isinstance(pool, Error):
            self.error = pool
            self.state = ScalesetState.halt
            self.save()
            return

        if pool.state == PoolState.init:
            logging.info(
                "scaleset waiting for pool: %s - %s", self.pool_name, self.scaleset_id
            )
        elif pool.state == PoolState.running:
            image_os = get_os(self.region, self.image)
            if isinstance(image_os, Error):
                self.error = image_os
                self.state = ScalesetState.creation_failed
            elif image_os != pool.os:
                self.error = Error(
                    code=ErrorCode.INVALID_REQUEST,
                    errors=["invalid os (got: %s needed: %s)" % (image_os, pool.os)],
                )
                self.state = ScalesetState.creation_failed
            else:
                self.state = ScalesetState.setup
        else:
            self.state = ScalesetState.setup

        self.save()

    def setup(self) -> None:
        # TODO: How do we pass in SSH configs for Windows?  Previously
        # This was done as part of the generated per-task setup script.
        logging.info("scaleset setup: %s", self.scaleset_id)

        network = Network(self.region)
        network_id = network.get_id()
        if not network_id:
            logging.info("creating network: %s", self.region)
            result = network.create()
            if isinstance(result, Error):
                self.error = result
                self.state = ScalesetState.creation_failed
            self.save()
            return

        if self.auth is None:
            self.error = Error(
                code=ErrorCode.UNABLE_TO_CREATE, errors=["missing required auth"]
            )
            self.state = ScalesetState.creation_failed
            self.save()
            return

        vmss = get_vmss(self.scaleset_id)
        if vmss is None:
            pool = Pool.get_by_name(self.pool_name)
            if isinstance(pool, Error):
                self.error = pool
                self.state = ScalesetState.halt
                self.save()
                return

            logging.info("creating scaleset: %s", self.scaleset_id)
            extensions = fuzz_extensions(self.region, pool.os, self.pool_name)
            result = create_vmss(
                self.region,
                self.scaleset_id,
                self.vm_sku,
                self.size,
                self.image,
                network_id,
                self.spot_instances,
                extensions,
                self.auth.password,
                self.auth.public_key,
                self.tags,
            )
            if isinstance(result, Error):
                self.error = result
                logging.error(
                    "stopping task because of failed vmss: %s %s",
                    self.scaleset_id,
                    result,
                )
                self.state = ScalesetState.creation_failed
            else:
                logging.info("creating scaleset: %s", self.scaleset_id)
        elif vmss.provisioning_state == "Creating":
            logging.info("Waiting on scaleset creation: %s", self.scaleset_id)
            if vmss.identity and vmss.identity.principal_id:
                self.client_object_id = vmss.identity.principal_id
        else:
            logging.info("scaleset running: %s", self.scaleset_id)
            self.state = ScalesetState.running
            self.client_object_id = vmss.identity.principal_id
        self.save()

    # result = 'did I modify the scaleset in azure'
    def cleanup_nodes(self) -> bool:
        if self.state == ScalesetState.halt:
            logging.info("halting scaleset: %s", self.scaleset_id)
            self.halt()
            return True

        to_reimage = []
        to_delete = []

        nodes = Node.search_states(
            scaleset_id=self.scaleset_id, states=NodeState.ready_for_reset()
        )

        if not nodes:
            logging.info("no nodes need updating: %s", self.scaleset_id)
            return False

        # ground truth of existing nodes
        azure_nodes = list_instance_ids(self.scaleset_id)

        for node in nodes:
            if node.machine_id not in azure_nodes:
                logging.info(
                    "no longer in scaleset: %s:%s", self.scaleset_id, node.machine_id
                )
                node.delete()
            elif node.delete_requested:
                to_delete.append(node)
            else:
                if ScalesetShrinkQueue(self.scaleset_id).should_shrink():
                    node.set_halt()
                    to_delete.append(node)
                elif not node.reimage_queued:
                    # only add nodes that are not already set to reschedule
                    to_reimage.append(node)

        # Perform operations until they fail due to scaleset getting locked
        try:
            if to_delete:
                logging.info(
                    "deleting nodes: %s - count: %d", self.scaleset_id, len(to_delete)
                )
                self.delete_nodes(to_delete)
                for node in to_delete:
                    node.set_halt()
                    node.state = NodeState.halt
                    node.save()

            if to_reimage:
                self.reimage_nodes(to_reimage)
        except UnableToUpdate:
            logging.info("scaleset update already in progress: %s", self.scaleset_id)

        return True

    def _resize_equal(self) -> None:
        # NOTE: this is the only place we reset to the 'running' state.
        # This ensures that our idea of scaleset size agrees with Azure
        node_count = len(Node.search_states(scaleset_id=self.scaleset_id))
        if node_count == self.size:
            logging.info("resize finished: %s", self.scaleset_id)
            self.state = ScalesetState.running
            self.save()
            return
        else:
            logging.info(
                "resize is finished, waiting for nodes to check in: "
                "%s (%d of %d nodes checked in)",
                self.scaleset_id,
                node_count,
                self.size,
            )
            return

    def _resize_grow(self) -> None:
        try:
            resize_vmss(self.scaleset_id, self.size)
        except UnableToUpdate:
            logging.info("scaleset is mid-operation already")
        return

    def _resize_shrink(self, to_remove: int) -> None:
        queue = ScalesetShrinkQueue(self.scaleset_id)
        for _ in range(to_remove):
            queue.add_entry()

    def resize(self) -> None:
        # no longer needing to resize
        if self.state != ScalesetState.resize:
            return

        logging.info("scaleset resize: %s - %s", self.scaleset_id, self.size)

        # reset the node delete queue
        ScalesetShrinkQueue(self.scaleset_id).clear()

        # just in case, always ensure size is within max capacity
        self.size = min(self.size, self.max_size())

        # Treat Azure knowledge of the size of the scaleset as "ground truth"
        size = get_vmss_size(self.scaleset_id)
        if size is None:
            logging.info("scaleset is unavailable: %s", self.scaleset_id)
            return

        if size == self.size:
            self._resize_equal()
        elif self.size > size:
            self._resize_grow()
        else:
            self._resize_shrink(size - self.size)

    def delete_nodes(self, nodes: List[Node]) -> None:
        if not nodes:
            logging.debug("no nodes to delete")
            return

        if self.state == ScalesetState.halt:
            logging.debug("scaleset delete will delete node: %s", self.scaleset_id)
            return

        machine_ids = [x.machine_id for x in nodes]

        logging.info("deleting %s:%s", self.scaleset_id, machine_ids)
        delete_vmss_nodes(self.scaleset_id, machine_ids)

    def reimage_nodes(self, nodes: List[Node]) -> None:
        if not nodes:
            logging.debug("no nodes to reimage")
            return

        if self.state == ScalesetState.shutdown:
            self.delete_nodes(nodes)
            return

        if self.state == ScalesetState.halt:
            logging.debug("scaleset delete will delete node: %s", self.scaleset_id)
            return

        machine_ids = [x.machine_id for x in nodes]

        result = reimage_vmss_nodes(self.scaleset_id, machine_ids)
        if isinstance(result, Error):
            raise Exception(
                "unable to reimage nodes: %s:%s - %s"
                % (self.scaleset_id, machine_ids, result)
            )
        for node in nodes:
            node.reimage_queued = True
            node.save()

    def shutdown(self) -> None:
        size = get_vmss_size(self.scaleset_id)
        logging.info("scaleset shutdown: %s (current size: %s)", self.scaleset_id, size)
        if size is None or size == 0:
            self.halt()

    def halt(self) -> None:
        self.state = ScalesetState.halt
        ScalesetShrinkQueue(self.scaleset_id).delete()

        for node in Node.search_states(scaleset_id=self.scaleset_id):
            logging.info("deleting node %s:%s", self.scaleset_id, node.machine_id)
            node.delete()

        vmss = get_vmss(self.scaleset_id)
        if vmss:
            logging.info("scaleset deleting: %s", self.scaleset_id)
            delete_vmss(self.scaleset_id)
            self.save()
        else:
            logging.info("scaleset deleted: %s", self.scaleset_id)
            self.delete()

    @classmethod
    def scaleset_max_size(cls, image: str) -> int:
        # https://docs.microsoft.com/en-us/azure/virtual-machine-scale-sets/
        #   virtual-machine-scale-sets-placement-groups#checklist-for-using-large-scale-sets
        if image.startswith("/"):
            return 600
        else:
            return 1000

    def max_size(self) -> int:
        return Scaleset.scaleset_max_size(self.image)

    @classmethod
    def search_states(
        cls, *, states: Optional[List[ScalesetState]] = None
    ) -> List["Scaleset"]:
        query: QueryFilter = {}
        if states:
            query["state"] = states
        return cls.search(query=query)

    def update_nodes(self) -> None:
        # Be in at-least 'setup' before checking for the list of VMs
        if self.state == self.init:
            return

        nodes = Node.search_states(scaleset_id=self.scaleset_id)
        azure_nodes = list_instance_ids(self.scaleset_id)

        self.nodes = []

        for (machine_id, instance_id) in azure_nodes.items():
            node_state: Optional[ScalesetNodeState] = None
            for node in nodes:
                if node.machine_id == machine_id:
                    node_state = ScalesetNodeState(
                        machine_id=machine_id,
                        instance_id=instance_id,
                        state=node.state,
                    )
                    break
            if not node_state:
                node_state = ScalesetNodeState(
                    machine_id=machine_id,
                    instance_id=instance_id,
                )
            self.nodes.append(node_state)

    def update_configs(self) -> None:
        if self.state != ScalesetState.running:
            logging.debug(
                "scaleset not running, not updating configs: %s", self.scaleset_id
            )
            return

        pool = Pool.get_by_name(self.pool_name)
        if isinstance(pool, Error):
            self.error = pool
            return self.halt()

        logging.debug("updating scaleset configs: %s", self.scaleset_id)
        extensions = fuzz_extensions(self.region, pool.os, self.pool_name)
        try:
            update_extensions(self.scaleset_id, extensions)
        except UnableToUpdate:
            logging.debug(
                "unable to update configs, update already in progress: %s",
                self.scaleset_id,
            )

    @classmethod
    def key_fields(cls) -> Tuple[str, str]:
        return ("pool_name", "scaleset_id")


class ShrinkEntry(BaseModel):
    shrink_id: UUID = Field(default_factory=uuid4)


class ScalesetShrinkQueue:
    def __init__(self, scaleset_id: UUID):
        self.scaleset_id = scaleset_id

    def queue_name(self) -> str:
        return "to-shrink-%s" % self.scaleset_id.hex

    def clear(self) -> None:
        clear_queue(self.queue_name(), account_id=get_func_storage())

    def create(self) -> None:
        create_queue(self.queue_name(), account_id=get_func_storage())

    def delete(self) -> None:
        delete_queue(self.queue_name(), account_id=get_func_storage())

    def add_entry(self) -> None:
        queue_object(self.queue_name(), ShrinkEntry(), account_id=get_func_storage())

    def should_shrink(self) -> bool:
        return remove_first_message(self.queue_name(), account_id=get_func_storage())
