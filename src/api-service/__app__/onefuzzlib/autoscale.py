#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import math
from typing import List

from onefuzztypes.enums import NodeState, ScalesetState
from onefuzztypes.models import AutoScaleConfig, TaskPool

from .tasks.main import Task
from .workers.nodes import Node
from .workers.pools import Pool
from .workers.scalesets import Scaleset


def scale_up(pool: Pool, scalesets: List[Scaleset], nodes_needed: int) -> None:
    logging.info("Scaling up")
    autoscale_config = pool.autoscale
    if not isinstance(autoscale_config, AutoScaleConfig):
        return

    for scaleset in scalesets:
        if scaleset.state in [ScalesetState.running, ScalesetState.resize]:

            max_size = min(scaleset.max_size(), autoscale_config.scaleset_size)
            logging.info(
                "scaleset:%s size:%d max_size:%d",
                scaleset.scaleset_id,
                scaleset.size,
                max_size,
            )
            if scaleset.size < max_size:
                current_size = scaleset.size
                if nodes_needed <= max_size - current_size:
                    scaleset.set_size(current_size + nodes_needed)
                    nodes_needed = 0
                else:
                    scaleset.set_size(max_size)
                    nodes_needed = nodes_needed - (max_size - current_size)

            else:
                continue

            if nodes_needed == 0:
                return

    for _ in range(
        math.ceil(
            nodes_needed
            / min(
                Scaleset.scaleset_max_size(autoscale_config.image),
                autoscale_config.scaleset_size,
            )
        )
    ):
        logging.info("Creating Scaleset for Pool %s", pool.name)
        max_nodes_scaleset = min(
            Scaleset.scaleset_max_size(autoscale_config.image),
            autoscale_config.scaleset_size,
            nodes_needed,
        )

        if not autoscale_config.region:
            raise Exception("Region is missing")

        Scaleset.create(
            pool_name=pool.name,
            vm_sku=autoscale_config.vm_sku,
            image=autoscale_config.image,
            region=autoscale_config.region,
            size=max_nodes_scaleset,
            spot_instances=autoscale_config.spot_instances,
            ephemeral_os_disks=autoscale_config.ephemeral_os_disks,
            tags={"pool": pool.name},
            extensions=[],
            cert_key="",
            cert="",
        )
        nodes_needed -= max_nodes_scaleset


def scale_down(scalesets: List[Scaleset], nodes_to_remove: int) -> None:
    logging.info("Scaling down")
    for scaleset in scalesets:
        num_of_nodes = len(Node.search_states(scaleset_id=scaleset.scaleset_id))
        if scaleset.size != num_of_nodes and scaleset.state not in [
            ScalesetState.resize,
            ScalesetState.shutdown,
            ScalesetState.halt,
        ]:
            scaleset.set_state(ScalesetState.resize)

        free_nodes = Node.search_states(
            scaleset_id=scaleset.scaleset_id,
            states=[NodeState.free],
        )
        nodes = []
        for node in free_nodes:
            if not node.delete_requested:
                nodes.append(node)
        logging.info("Scaleset: %s, #Free Nodes: %s", scaleset.scaleset_id, len(nodes))

        if nodes and nodes_to_remove > 0:
            max_nodes_remove = min(len(nodes), nodes_to_remove)
            # All nodes in scaleset are free. Can shutdown VMSS
            if max_nodes_remove >= scaleset.size and len(nodes) >= scaleset.size:
                scaleset.set_state(ScalesetState.shutdown)
                nodes_to_remove = nodes_to_remove - scaleset.size
                for node in nodes:
                    node.set_shutdown()
                continue

            # Resize of VMSS needed
            scaleset.set_size(scaleset.size - max_nodes_remove)
            nodes_to_remove = nodes_to_remove - max_nodes_remove
            scaleset.set_state(ScalesetState.resize)


def get_vm_count(tasks: List[Task]) -> int:
    count = 0
    for task in tasks:
        task_pool = task.get_pool()
        if (
            not task_pool
            or not isinstance(task_pool, Pool)
            or not isinstance(task.config.pool, TaskPool)
        ):
            continue
        count += task.config.pool.count
    return count


def autoscale_pool(pool: Pool) -> None:
    logging.info("autoscale: %s", pool.autoscale)
    if not pool.autoscale:
        return

    # get all the tasks (count not stopped) for the pool
    tasks = Task.get_tasks_by_pool_name(pool.name)
    logging.info("Pool: %s, #Tasks %d", pool.name, len(tasks))

    num_of_tasks = get_vm_count(tasks)
    nodes_needed = max(num_of_tasks, pool.autoscale.min_size)
    if pool.autoscale.max_size:
        nodes_needed = min(nodes_needed, pool.autoscale.max_size)

    # do scaleset logic match with pool
    # get all the scalesets for the pool
    scalesets = Scaleset.search_by_pool(pool.name)
    pool_resize = False
    for scaleset in scalesets:
        if scaleset.state in ScalesetState.modifying():
            pool_resize = True
            break
        nodes_needed = nodes_needed - scaleset.size

    if pool_resize:
        return

    logging.info("Pool: %s, #Nodes Needed: %d", pool.name, nodes_needed)
    if nodes_needed > 0:
        # resizing scaleset or creating new scaleset.
        scale_up(pool, scalesets, nodes_needed)
    elif nodes_needed < 0:
        for scaleset in scalesets:
            nodes = Node.search_states(scaleset_id=scaleset.scaleset_id)
            for node in nodes:
                if node.delete_requested:
                    nodes_needed += 1
    if nodes_needed < 0:
        scale_down(scalesets, abs(nodes_needed))
