#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

# NOTE: Set ONEFUZZ_SCALESET_MAX_SIZE environment variable to artificially set
# the maximum size of a scaleset for testing.

import logging
import os
from typing import List

from onefuzztypes.enums import ScalesetState

from ..azure.creds import get_base_region
from ..tasks.main import Task
from .nodes import Node
from .pools import Pool
from .scalesets import Scaleset
from .shrink_queue import ShrinkQueue


def set_shrink_queues(pool: Pool, scalesets: List[Scaleset], size: int) -> None:
    for scaleset in scalesets:
        ShrinkQueue(scaleset.scaleset_id).clear()

    ShrinkQueue(pool.pool_id).set_size(size)


def scale_up(pool: Pool, scalesets: List[Scaleset], to_add: int) -> None:
    logging.info(
        "autoscale up - pool:%s to_add:%d scalesets:%s",
        pool,
        to_add,
        [x.scaleset_id for x in scalesets],
    )

    config = pool.autoscale
    if not config:
        raise Exception(f"scaling up a non-autoscaling pool: {pool.name}")

    set_shrink_queues(pool, scalesets, 0)

    for scaleset in sorted(scalesets, key=lambda x: x.scaleset_id):
        if to_add <= 0:
            break

        if scaleset.state in ScalesetState.can_resize():
            scaleset_max_size = scaleset.max_size()
            if scaleset.size < scaleset_max_size:
                scaleset_to_add = min(to_add, scaleset_max_size - scaleset.size)
                logging.info(
                    "autoscale adding to scaleset: "
                    "pool:%s scaleset:%s existing_size:%d adding:%d",
                    pool.name,
                    scaleset.scaleset_id,
                    scaleset.size,
                    scaleset_to_add,
                )
                scaleset.size += scaleset_to_add
                scaleset.state = ScalesetState.resize
                scaleset.save()
                to_add -= scaleset_to_add

    region = config.region or get_base_region()
    base_size = Scaleset.scaleset_max_size(config.image)

    alternate_max_size = os.environ.get("ONEFUZZ_SCALESET_MAX_SIZE")
    if alternate_max_size is not None:
        base_size = min(base_size, int(alternate_max_size))

    while to_add > 0:
        scaleset_size = min(base_size, to_add)
        logging.info(
            "autoscale adding scaleset.  pool:%s size:%s", pool.name, scaleset_size
        )
        scaleset = Scaleset.create(
            pool_name=pool.name,
            vm_sku=config.vm_sku,
            image=config.image,
            region=region,
            size=scaleset_size,
            spot_instances=config.spot_instances,
            tags={"pool": pool.name},
        )
        logging.info("autoscale added scaleset:%s", scaleset.scaleset_id)
        to_add -= scaleset_size


def shutdown_empty_scalesets(pool: Pool, scalesets: List[Scaleset]) -> None:
    for scaleset in scalesets:
        nodes = Node.search_states(scaleset_id=scaleset.scaleset_id)

        if (
            not nodes
            and scaleset.size == 0
            and scaleset.state not in ScalesetState.needs_work()
        ):
            logging.info(
                "autoscale halting empty scaleset.  pool:%s scaleset:%s",
                pool.name,
                scaleset.scaleset_id,
            )
            scaleset.halt()


def scale_down(pool: Pool, scalesets: List[Scaleset], to_remove: int) -> None:
    logging.info(
        "autoscale down - pool:%s to_remove:%d scalesets:%s",
        pool,
        to_remove,
        [x.scaleset_id for x in scalesets],
    )

    set_shrink_queues(pool, scalesets, to_remove)


def get_tasks_vm_count(tasks: List[Task]) -> int:
    count = 0
    for task in tasks:
        if task.config.pool:
            count += task.config.pool.count

        if task.config.vm:
            count += task.config.vm.count

    return count


def autoscale_pool(pool: Pool) -> None:
    logging.info("autoscale: %s", pool.autoscale)
    if not pool.autoscale:
        return

    # get all the tasks (count not stopped) for the pool
    tasks = Task.get_tasks_by_pool_name(pool.name)
    logging.info("Pool: %s, #Tasks %d", pool.name, len(tasks))

    num_of_tasks = get_tasks_vm_count(tasks)
    new_size = max(num_of_tasks, pool.autoscale.min_size)
    if pool.autoscale.max_size:
        new_size = min(new_size, pool.autoscale.max_size)

    # do scaleset logic match with pool
    # get all the scalesets for the pool
    scalesets = Scaleset.search_by_pool(pool.name)
    current_size = 0
    for scaleset in scalesets:
        modifying = [
            x.scaleset_id for x in scalesets if x.state in ScalesetState.modifying()
        ]
        if modifying:
            logging.info(
                "pool has modifying scalesets, unable to autoscale: %s - %s",
                pool.name,
                modifying,
            )
            return
        current_size += scaleset.size

    logging.info(
        "autoscale pool %s - current_size: %d new_size: %d",
        pool.name,
        current_size,
        new_size,
    )

    if new_size > current_size:
        scale_up(pool, scalesets, new_size - current_size)
    elif current_size > new_size:
        scale_down(pool, scalesets, current_size - new_size)
        shutdown_empty_scalesets(pool, scalesets)
    else:
        shutdown_empty_scalesets(pool, scalesets)
