#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import logging
from typing import Any, Dict, List, Optional, Tuple, Union
from uuid import UUID, uuid4

from onefuzztypes.enums import ErrorCode, NodeState, PoolState, ScalesetState
from onefuzztypes.events import (
    EventScalesetCreated,
    EventScalesetDeleted,
    EventScalesetFailed,
    EventScalesetStateUpdated,
)
from onefuzztypes.models import Error
from onefuzztypes.models import Scaleset as BASE_SCALESET
from onefuzztypes.models import ScalesetNodeState
from onefuzztypes.primitives import PoolName, Region
from pydantic import BaseModel, Field

from ..__version__ import __version__
from ..azure.auth import build_auth
from ..azure.image import get_os
from ..azure.network import Network
from ..azure.queue import (
    clear_queue,
    create_queue,
    delete_queue,
    queue_object,
    remove_first_message,
)
from ..azure.storage import StorageType
from ..azure.vmss import (
    UnableToUpdate,
    create_vmss,
    delete_vmss,
    delete_vmss_nodes,
    get_vmss,
    get_vmss_size,
    list_instance_ids,
    reimage_vmss_nodes,
    resize_vmss,
    update_extensions,
)
from ..events import send_event
from ..extension import fuzz_extensions
from ..orm import MappingIntStrAny, ORMMixin, QueryFilter
from .nodes import Node

NODE_EXPIRATION_TIME: datetime.timedelta = datetime.timedelta(hours=1)
NODE_REIMAGE_TIME: datetime.timedelta = datetime.timedelta(days=7)
SCALESET_LOG_PREFIX = "scalesets: "

# Future work:
#
# Enabling autoscaling for the scalesets based on the pool work queues.
# https://docs.microsoft.com/en-us/azure/azure-monitor/platform/autoscale-common-metrics#commonly-used-storage-metrics


class Scaleset(BASE_SCALESET, ORMMixin):
    def save_exclude(self) -> Optional[MappingIntStrAny]:
        return {"nodes": ...}

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
        ephemeral_os_disks: bool,
        tags: Dict[str, str],
        client_id: Optional[UUID] = None,
        client_object_id: Optional[UUID] = None,
    ) -> "Scaleset":
        entry = cls(
            pool_name=pool_name,
            vm_sku=vm_sku,
            image=image,
            region=region,
            size=size,
            spot_instances=spot_instances,
            ephemeral_os_disks=ephemeral_os_disks,
            auth=build_auth(),
            client_id=client_id,
            client_object_id=client_object_id,
            tags=tags,
        )
        entry.save()
        send_event(
            EventScalesetCreated(
                scaleset_id=entry.scaleset_id,
                pool_name=entry.pool_name,
                vm_sku=vm_sku,
                image=image,
                region=region,
                size=size,
            )
        )
        return entry

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

    def set_failed(self, error: Error) -> None:
        if self.error is not None:
            return

        self.error = error
        self.set_state(ScalesetState.creation_failed)

        send_event(
            EventScalesetFailed(
                scaleset_id=self.scaleset_id, pool_name=self.pool_name, error=self.error
            )
        )

    def init(self) -> None:
        from .pools import Pool

        logging.info(SCALESET_LOG_PREFIX + "init. scaleset_id:%s", self.scaleset_id)

        ScalesetShrinkQueue(self.scaleset_id).create()

        # Handle the race condition between a pool being deleted and a
        # scaleset being added to the pool.
        pool = Pool.get_by_name(self.pool_name)
        if isinstance(pool, Error):
            self.set_failed(pool)
            return

        if pool.state == PoolState.init:
            logging.info(
                SCALESET_LOG_PREFIX + "waiting for pool. pool_name:%s scaleset_id:%s",
                self.pool_name,
                self.scaleset_id,
            )
        elif pool.state == PoolState.running:
            image_os = get_os(self.region, self.image)
            if isinstance(image_os, Error):
                self.set_failed(image_os)
                return

            elif image_os != pool.os:
                error = Error(
                    code=ErrorCode.INVALID_REQUEST,
                    errors=["invalid os (got: %s needed: %s)" % (image_os, pool.os)],
                )
                self.set_failed(error)
                return
            else:
                self.set_state(ScalesetState.setup)
        else:
            self.set_state(ScalesetState.setup)

    def setup(self) -> None:
        from .pools import Pool

        # TODO: How do we pass in SSH configs for Windows?  Previously
        # This was done as part of the generated per-task setup script.
        logging.info(SCALESET_LOG_PREFIX + "setup. scalset_id:%s", self.scaleset_id)

        network = Network(self.region)
        network_id = network.get_id()
        if not network_id:
            logging.info(
                SCALESET_LOG_PREFIX + "creating network. region:%s scaleset_id:%s",
                self.region,
                self.scaleset_id,
            )
            result = network.create()
            if isinstance(result, Error):
                self.set_failed(result)
                return
            self.save()
            return

        if self.auth is None:
            error = Error(
                code=ErrorCode.UNABLE_TO_CREATE, errors=["missing required auth"]
            )
            self.set_failed(error)
            return

        vmss = get_vmss(self.scaleset_id)
        if vmss is None:
            pool = Pool.get_by_name(self.pool_name)
            if isinstance(pool, Error):
                self.set_failed(pool)
                return

            logging.info(
                SCALESET_LOG_PREFIX + "creating scaleset. scaleset_id:%s",
                self.scaleset_id,
            )
            extensions = fuzz_extensions(pool, self)
            result = create_vmss(
                self.region,
                self.scaleset_id,
                self.vm_sku,
                self.size,
                self.image,
                network_id,
                self.spot_instances,
                self.ephemeral_os_disks,
                extensions,
                self.auth.password,
                self.auth.public_key,
                self.tags,
            )
            if isinstance(result, Error):
                self.set_failed(result)
                return
            else:
                logging.info(
                    SCALESET_LOG_PREFIX + "creating scaleset scaleset_id:%s",
                    self.scaleset_id,
                )
        elif vmss.provisioning_state == "Creating":
            logging.info(
                SCALESET_LOG_PREFIX + "Waiting on scaleset creation scalset_id:%s",
                self.scaleset_id,
            )
            self.try_set_identity(vmss)
        else:
            logging.info(
                SCALESET_LOG_PREFIX + "scaleset running scaleset_id:%s",
                self.scaleset_id,
            )
            identity_result = self.try_set_identity(vmss)
            if identity_result:
                self.set_failed(identity_result)
                return
            else:
                self.set_state(ScalesetState.running)
        self.save()

    def try_set_identity(self, vmss: Any) -> Optional[Error]:
        def get_error() -> Error:
            return Error(
                code=ErrorCode.VM_CREATE_FAILED,
                errors=[
                    "The scaleset is expected to have exactly 1 user assigned identity"
                ],
            )

        if self.client_object_id:
            return None
        if (
            vmss.identity
            and vmss.identity.user_assigned_identities
            and (len(vmss.identity.user_assigned_identities) != 1)
        ):
            return get_error()

        user_assinged_identities = list(vmss.identity.user_assigned_identities.values())

        if user_assinged_identities[0].principal_id:
            self.client_object_id = user_assinged_identities[0].principal_id
            return None
        else:
            return get_error()

    # result = 'did I modify the scaleset in azure'
    def cleanup_nodes(self) -> bool:
        if self.state == ScalesetState.halt:
            logging.info(
                SCALESET_LOG_PREFIX + "halting scaleset scaleset_id:%s",
                self.scaleset_id,
            )
            self.halt()
            return True

        Node.reimage_long_lived_nodes(self.scaleset_id)

        to_reimage = []
        to_delete = []

        # ground truth of existing nodes
        azure_nodes = list_instance_ids(self.scaleset_id)

        nodes = Node.search_states(scaleset_id=self.scaleset_id)

        # Nodes do not exists in scalesets but in table due to unknown failure
        for node in nodes:
            if node.machine_id not in azure_nodes:
                logging.info(
                    SCALESET_LOG_PREFIX
                    + "no longer in scaleset. scaleset_id:%s machine_id:%s",
                    self.scaleset_id,
                    node.machine_id,
                )
                node.delete()

        # Scalesets can have nodes that never check in (such as broken OS setup
        # scripts).
        #
        # This will add nodes that Azure knows about but have not checked in
        # such that the `dead node` detection will eventually reimage the node.
        #
        # NOTE: If node setup takes longer than NODE_EXPIRATION_TIME (1 hour),
        # this will cause the nodes to continuously get reimaged.
        node_machine_ids = [x.machine_id for x in nodes]
        for machine_id in azure_nodes:
            if machine_id in node_machine_ids:
                continue

            logging.info(
                SCALESET_LOG_PREFIX
                + "adding missing azure node. scaleset_id:%s machine_id:%s",
                self.scaleset_id,
                machine_id,
            )

            # Note, using `new=True` makes it such that if a node already has
            # checked in, this won't overwrite it.
            Node.create(
                pool_name=self.pool_name,
                machine_id=machine_id,
                scaleset_id=self.scaleset_id,
                version=__version__,
                new=True,
            )

        existing_nodes = [x for x in nodes if x.machine_id in azure_nodes]
        nodes_to_reset = [
            x for x in existing_nodes if x.state in NodeState.ready_for_reset()
        ]

        for node in nodes_to_reset:
            if node.delete_requested:
                to_delete.append(node)
            else:
                if ScalesetShrinkQueue(self.scaleset_id).should_shrink():
                    node.set_halt()
                    to_delete.append(node)
                elif not node.reimage_queued:
                    # only add nodes that are not already set to reschedule
                    to_reimage.append(node)

        dead_nodes = Node.get_dead_nodes(self.scaleset_id, NODE_EXPIRATION_TIME)
        if dead_nodes:
            logging.info(
                SCALESET_LOG_PREFIX
                + "reimaging nodes with expired heartbeats. "
                + "scaleset_id:%s nodes:%s",
                self.scaleset_id,
                ",".join(str(x.machine_id) for x in dead_nodes),
            )
            for node in dead_nodes:
                if node not in to_reimage:
                    to_reimage.append(node)

        # Perform operations until they fail due to scaleset getting locked
        try:
            if to_delete:
                logging.info(
                    SCALESET_LOG_PREFIX + "deleting nodes. scaleset_id:%s count:%d",
                    self.scaleset_id,
                    len(to_delete),
                )
                self.delete_nodes(to_delete)
                for node in to_delete:
                    node.set_halt()

            if to_reimage:
                logging.info(
                    SCALESET_LOG_PREFIX + "reimaging nodes: scaleset_id:%s count:%d",
                    self.scaleset_id,
                    len(to_reimage),
                )
                self.reimage_nodes(to_reimage)
        except UnableToUpdate:
            logging.info(
                SCALESET_LOG_PREFIX
                + "scaleset update already in progress: scaleset_id:%s",
                self.scaleset_id,
            )

        return bool(to_reimage) or bool(to_delete)

    def _resize_equal(self) -> None:
        # NOTE: this is the only place we reset to the 'running' state.
        # This ensures that our idea of scaleset size agrees with Azure
        node_count = len(Node.search_states(scaleset_id=self.scaleset_id))
        if node_count == self.size:
            logging.info(SCALESET_LOG_PREFIX + "resize finished: %s", self.scaleset_id)
            self.set_state(ScalesetState.running)
        else:
            logging.info(
                SCALESET_LOG_PREFIX
                + "resize is finished, waiting for nodes to check in: "
                "scaleset_id:%s (%d of %d nodes checked in)",
                self.scaleset_id,
                node_count,
                self.size,
            )

    def _resize_grow(self) -> None:
        try:
            resize_vmss(self.scaleset_id, self.size)
        except UnableToUpdate:
            logging.info(
                SCALESET_LOG_PREFIX
                + "scaleset is mid-operation already scaleset_id:%s",
                self.scaleset_id,
            )
        return

    def _resize_shrink(self, to_remove: int) -> None:
        logging.info(
            SCALESET_LOG_PREFIX + "shrinking scaleset. scaleset_id:%s to_remove:%d",
            self.scaleset_id,
            to_remove,
        )
        queue = ScalesetShrinkQueue(self.scaleset_id)
        for _ in range(to_remove):
            queue.add_entry()

        nodes = Node.search_states(scaleset_id=self.scaleset_id)
        for node in nodes:
            node.send_stop_if_free()

    def resize(self) -> None:
        # no longer needing to resize
        if self.state != ScalesetState.resize:
            return

        logging.info(
            SCALESET_LOG_PREFIX + "scaleset resize: scaleset_id:%s size:%d",
            self.scaleset_id,
            self.size,
        )

        # reset the node delete queue
        ScalesetShrinkQueue(self.scaleset_id).clear()

        # just in case, always ensure size is within max capacity
        self.size = min(self.size, self.max_size())

        # Treat Azure knowledge of the size of the scaleset as "ground truth"
        size = get_vmss_size(self.scaleset_id)
        if size is None:
            logging.info(
                SCALESET_LOG_PREFIX + "scaleset is unavailable. scaleset_id:%s",
                self.scaleset_id,
            )
            # if the scaleset is missing, this is an indication the scaleset
            # was manually deleted, rather than having OneFuzz delete it.  As
            # such, we should go thruogh the process of deleting it.
            self.set_shutdown(now=True)
            return

        if size == self.size:
            self._resize_equal()
        elif self.size > size:
            self._resize_grow()
        else:
            self._resize_shrink(size - self.size)

    def delete_nodes(self, nodes: List[Node]) -> None:
        if not nodes:
            logging.info(
                SCALESET_LOG_PREFIX + "no nodes to delete. scaleset_id:%s",
                self.scaleset_id,
            )
            return

        if self.state == ScalesetState.halt:
            logging.info(
                SCALESET_LOG_PREFIX
                + "scaleset halting, ignoring node deletion: scaleset_id:%s",
                self.scaleset_id,
            )
            return

        machine_ids = []
        for node in nodes:
            if node.debug_keep_node:
                logging.warning(
                    SCALESET_LOG_PREFIX + "not deleting manually overridden node. "
                    "scaleset_id:%s machine_id:%s",
                    self.scaleset_id,
                    node.machine_id,
                )
            else:
                machine_ids.append(node.machine_id)

        logging.info(
            SCALESET_LOG_PREFIX + "deleting nodes scaleset_id:%s machine_id:%s",
            self.scaleset_id,
            machine_ids,
        )
        delete_vmss_nodes(self.scaleset_id, machine_ids)

    def reimage_nodes(self, nodes: List[Node]) -> None:
        if not nodes:
            logging.info(
                SCALESET_LOG_PREFIX + "no nodes to reimage: scaleset_id:%s",
                self.scaleset_id,
            )
            return

        if self.state == ScalesetState.shutdown:
            logging.info(
                SCALESET_LOG_PREFIX
                + "scaleset shutting down, deleting rather than reimaging nodes. "
                + "scaleset_id:%s",
                self.scaleset_id,
            )
            self.delete_nodes(nodes)
            return

        if self.state == ScalesetState.halt:
            logging.info(
                SCALESET_LOG_PREFIX
                + "scaleset halting, ignoring node reimage: scaleset_id:%s",
                self.scaleset_id,
            )
            return

        machine_ids = []
        for node in nodes:
            if node.debug_keep_node:
                logging.warning(
                    SCALESET_LOG_PREFIX + "not reimaging manually overridden node. "
                    "scaleset_id:%s machine_id:%s",
                    self.scaleset_id,
                    node.machine_id,
                )
            else:
                machine_ids.append(node.machine_id)

        result = reimage_vmss_nodes(self.scaleset_id, machine_ids)
        if isinstance(result, Error):
            raise Exception(
                "unable to reimage nodes: %s:%s - %s"
                % (self.scaleset_id, machine_ids, result)
            )
        for node in nodes:
            node.reimage_queued = True
            node.save()

    def set_shutdown(self, now: bool) -> None:
        if self.state in [ScalesetState.halt, ScalesetState.shutdown]:
            return

        logging.info(
            SCALESET_LOG_PREFIX + "scaleset set_shutdown: scaleset_id:%s now:%s",
            self.scaleset_id,
            now,
        )

        if now:
            self.set_state(ScalesetState.halt)
        else:
            self.set_state(ScalesetState.shutdown)

        self.save()

    def shutdown(self) -> None:
        size = get_vmss_size(self.scaleset_id)
        if size is None:
            logging.info(
                SCALESET_LOG_PREFIX
                + "scaleset shutdown: scaleset already deleted - scaleset_id:%s",
                self.scaleset_id,
            )
            self.halt()
            return

        logging.info(
            SCALESET_LOG_PREFIX + "scaleset shutdown: scaleset_id:%s size:%d",
            self.scaleset_id,
            size,
        )
        nodes = Node.search_states(scaleset_id=self.scaleset_id)
        for node in nodes:
            node.set_shutdown()
        if size == 0:
            self.halt()

    def halt(self) -> None:
        ScalesetShrinkQueue(self.scaleset_id).delete()

        for node in Node.search_states(scaleset_id=self.scaleset_id):
            logging.info(
                SCALESET_LOG_PREFIX + "deleting node scaleset_id:%s machine_id:%s",
                self.scaleset_id,
                node.machine_id,
            )
            node.delete()

        logging.info(
            SCALESET_LOG_PREFIX + "scaleset delete starting: scaleset_id:%s",
            self.scaleset_id,
        )
        if delete_vmss(self.scaleset_id):
            logging.info(
                SCALESET_LOG_PREFIX + "scaleset deleted: scaleset_id:%s",
                self.scaleset_id,
            )
            self.delete()
        else:
            self.save()

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
        if self.state == ScalesetState.init:
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
        from .pools import Pool

        if self.state == ScalesetState.halt:
            logging.info(
                SCALESET_LOG_PREFIX
                + "not updating configs, scaleset is set to be deleted. "
                "scaleset_id:%s",
                self.scaleset_id,
            )
            return

        if not self.needs_config_update:
            logging.debug(
                SCALESET_LOG_PREFIX + "config update not needed. scaleset_id:%s",
                self.scaleset_id,
            )
            return

        logging.info(
            SCALESET_LOG_PREFIX + "updating scaleset configs. scaleset_id:%s",
            self.scaleset_id,
        )

        pool = Pool.get_by_name(self.pool_name)
        if isinstance(pool, Error):
            logging.error(
                SCALESET_LOG_PREFIX
                + "unable to find pool during config update. pool:%s scaleset_id:%s",
                pool,
                self.scaleset_id,
            )
            self.set_failed(pool)
            return

        extensions = fuzz_extensions(pool, self)
        try:
            update_extensions(self.scaleset_id, extensions)
            self.needs_config_update = False
            self.save()
        except UnableToUpdate:
            logging.info(
                SCALESET_LOG_PREFIX
                + "unable to update configs, update already in progress. "
                "scaleset_id:%s",
                self.scaleset_id,
            )

    @classmethod
    def key_fields(cls) -> Tuple[str, str]:
        return ("pool_name", "scaleset_id")

    def delete(self) -> None:
        super().delete()
        send_event(
            EventScalesetDeleted(scaleset_id=self.scaleset_id, pool_name=self.pool_name)
        )

    def set_state(self, state: ScalesetState) -> None:
        if self.state == state:
            return

        self.state = state
        self.save()
        send_event(
            EventScalesetStateUpdated(
                scaleset_id=self.scaleset_id, pool_name=self.pool_name, state=self.state
            )
        )


class ShrinkEntry(BaseModel):
    shrink_id: UUID = Field(default_factory=uuid4)


class ScalesetShrinkQueue:
    def __init__(self, scaleset_id: UUID):
        self.scaleset_id = scaleset_id

    def queue_name(self) -> str:
        return "to-shrink-%s" % self.scaleset_id.hex

    def clear(self) -> None:
        clear_queue(self.queue_name(), StorageType.config)

    def create(self) -> None:
        create_queue(self.queue_name(), StorageType.config)

    def delete(self) -> None:
        delete_queue(self.queue_name(), StorageType.config)

    def add_entry(self) -> None:
        queue_object(self.queue_name(), ShrinkEntry(), StorageType.config)

    def should_shrink(self) -> bool:
        return remove_first_message(self.queue_name(), StorageType.config)
