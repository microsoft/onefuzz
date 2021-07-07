#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

# NOTE: Set ONEFUZZ_SCALESET_MAX_SIZE environment variable to artificially set
# the maximum size of a scaleset for testing.

import logging
import os
from typing import List, Tuple

from onefuzztypes.enums import NodeState, ScalesetState
from onefuzztypes.models import WorkSet
from onefuzztypes.primitives import Container

from ..azure.containers import get_container_sas_url
from ..azure.creds import get_base_region
from ..azure.queue import decode_message, get_queue
from ..azure.storage import StorageType
from .nodes import Node
from .pools import Pool
from .scalesets import Scaleset
from .shrink_queue import ShrinkQueue

AUTOSCALE_LOG_PREFIX = "autoscale: "


def set_shrink_queues(pool: Pool, scalesets: List[Scaleset], size: int) -> None:
    for scaleset in scalesets:
        ShrinkQueue(scaleset.scaleset_id).clear()

    ShrinkQueue(pool.pool_id).set_size(size)


def scale_up(pool: Pool, scalesets: List[Scaleset], to_add: int) -> None:
    logging.info(
        AUTOSCALE_LOG_PREFIX + "scale up - pool:%s to_add:%d scalesets:%s",
        pool.name,
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

        if scaleset.state in ScalesetState.can_update():
            scaleset_max_size = scaleset.max_size()
            if scaleset.size < scaleset_max_size:
                scaleset_to_add = min(to_add, scaleset_max_size - scaleset.size)
                logging.info(
                    AUTOSCALE_LOG_PREFIX + "adding to scaleset: "
                    "pool:%s scaleset:%s existing_size:%d adding:%d",
                    pool.name,
                    scaleset.scaleset_id,
                    scaleset.size,
                    scaleset_to_add,
                )
                scaleset.set_size(scaleset.size + scaleset_to_add)
                to_add -= scaleset_to_add

    region = config.region or get_base_region()
    base_size = Scaleset.scaleset_max_size(config.image)

    alternate_max_size = os.environ.get("ONEFUZZ_SCALESET_MAX_SIZE")
    if alternate_max_size is not None:
        base_size = min(base_size, int(alternate_max_size))

    while to_add > 0:
        scaleset_size = min(base_size, to_add)
        logging.info(
            AUTOSCALE_LOG_PREFIX + "adding scaleset.  pool:%s size:%s",
            pool.name,
            scaleset_size,
        )
        scaleset = Scaleset.create(
            pool_name=pool.name,
            vm_sku=config.vm_sku,
            image=config.image,
            region=region,
            size=scaleset_size,
            spot_instances=config.spot_instances,
            ephemeral_os_disks=config.ephemeral_os_disks,
            tags={"pool": pool.name},
        )
        logging.info(
            AUTOSCALE_LOG_PREFIX + "added pool:%s scaleset:%s",
            pool.name,
            scaleset.scaleset_id,
        )
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
                AUTOSCALE_LOG_PREFIX + "halting empty scaleset.  pool:%s scaleset:%s",
                pool.name,
                scaleset.scaleset_id,
            )
            scaleset.halt()


def scale_down(pool: Pool, scalesets: List[Scaleset], to_remove: int) -> None:
    logging.info(
        AUTOSCALE_LOG_PREFIX + "scaling down - pool:%s to_remove:%d scalesets:%s",
        pool.name,
        to_remove,
        [x.scaleset_id for x in scalesets],
    )

    set_shrink_queues(pool, scalesets, to_remove)

    # TODO: this injects synthetic WorkSet entries into the pool queue to
    # trigger the nodes to reset faster
    #
    # This synthetic WorkSet uses the `tools` container as the workset setup
    # container.
    #
    # This should be revisited.
    if to_remove:
        container_sas = get_container_sas_url(
            Container("tools"), StorageType.config, read=True, list=True
        )

        workset = WorkSet(
            reboot=False, script=False, work_units=[], setup_url=container_sas
        )

        for _ in range(to_remove):
            pool.schedule_workset(workset)


def clear_synthetic_worksets(pool: Pool) -> None:
    client = get_queue(pool.get_pool_queue(), StorageType.corpus)
    if client is None:
        return

    deleted = 0
    ignored = 0

    keeping = []
    for message in client.receive_messages():
        decoded = decode_message(message, WorkSet)
        if not decoded:
            logging.warning(AUTOSCALE_LOG_PREFIX + "decode workset failed: %s", message)
            continue

        if decoded.work_units:
            keeping.append(message)
            ignored += 1
        else:
            client.delete_message(message)
            deleted += 1

    for message in keeping:
        client.update_message(message, visibility_timeout=0)

    logging.info(
        AUTOSCALE_LOG_PREFIX + "cleanup synthetic worksets.  ignored:%d deleted:%d",
        ignored,
        deleted,
    )


def needed_nodes(pool: Pool) -> Tuple[int, int]:
    # NOTE: queue peek only returns the first 30 objects.
    workset_queue = pool.peek_work_queue()
    # only count worksets with work
    scheduled_worksets = len([x for x in workset_queue if x.work_units])

    nodes = Node.search_states(pool_name=pool.name, states=NodeState.in_use())
    from_nodes = len(nodes)

    return (scheduled_worksets, from_nodes)


def autoscale_pool(pool: Pool) -> None:
    if not pool.autoscale:
        return

    scheduled_worksets, in_use_nodes = needed_nodes(pool)
    node_need_estimate = scheduled_worksets + in_use_nodes

    new_size = node_need_estimate
    if pool.autoscale.min_size is not None:
        new_size = max(node_need_estimate, pool.autoscale.min_size)
    if pool.autoscale.max_size is not None:
        new_size = min(new_size, pool.autoscale.max_size)

    scalesets = Scaleset.search_by_pool(pool.name)
    current_size = 0
    for scaleset in scalesets:
        valid_auto_scale_states = ScalesetState.include_autoscale_count()
        unable_to_autoscale = [
            x.scaleset_id for x in scalesets if x.state not in valid_auto_scale_states
        ]
        if unable_to_autoscale:
            logging.info(
                AUTOSCALE_LOG_PREFIX
                + "unable to autoscale pool due to modifying scalesets. "
                "pool:%s scalesets:%s",
                pool.name,
                unable_to_autoscale,
            )
            return
        current_size += scaleset.size

    logging.info(
        AUTOSCALE_LOG_PREFIX + "status - pool:%s current_size: %d new_size: %d "
        "(in-use nodes: %d, scheduled worksets: %d)",
        pool.name,
        current_size,
        new_size,
        in_use_nodes,
        scheduled_worksets,
    )

    if new_size > current_size:
        clear_synthetic_worksets(pool)
        scale_up(pool, scalesets, new_size - current_size)
    elif current_size > new_size:
        clear_synthetic_worksets(pool)
        scale_down(pool, scalesets, current_size - new_size)
        shutdown_empty_scalesets(pool, scalesets)
    else:
        shutdown_empty_scalesets(pool, scalesets)
