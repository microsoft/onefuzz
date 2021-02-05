#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

# NOTE: Set ONEFUZZ_SCALESET_MAX_SIZE environment variable to artificially set
# the maximum size of a scaleset for testing.

import logging
import os
from typing import List

from onefuzztypes.enums import NodeState, ScalesetState

from ..azure.creds import get_base_region
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


def needed_nodes(pool: Pool) -> int:
    count = 0

    # NOTE: queue peek only returns the first 30 objects.
    workset_queue = pool.peek_work_queue()
    count += len(workset_queue)

    nodes = Node.search_states(pool_name=pool.name, states=NodeState.in_use())
    count += len(nodes)

    return count


def autoscale_pool(pool: Pool) -> None:
    if not pool.autoscale:
        return
    logging.info("autoscale pool.  pool:%s config:%s", pool.name, pool.autoscale.json())

    node_need_estimate = needed_nodes(pool)
    logging.info(
        "autoscale pool estimate.  pool:%s estimate:%d", pool.name, node_need_estimate
    )

    new_size = max(node_need_estimate, pool.autoscale.min_size)
    if pool.autoscale.max_size:
        new_size = min(new_size, pool.autoscale.max_size)

    scalesets = Scaleset.search_by_pool(pool.name)
    current_size = 0
    for scaleset in scalesets:
        unable_to_autoscale = [
            x.scaleset_id
            for x in scalesets
            if x.state not in ScalesetState.include_autoscale_count()
        ]
        if unable_to_autoscale:
            logging.info(
                "autoscale - pool has modifying scalesets, "
                "unable to autoscale: %s - %s",
                pool.name,
                unable_to_autoscale,
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
