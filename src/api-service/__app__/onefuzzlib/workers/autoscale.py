#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

# NOTE: Set ONEFUZZ_SCALESET_MAX_SIZE environment variable to artificially set
# the maximum size of a scaleset for testing.

import logging
from typing import List, Tuple

from onefuzztypes.enums import NodeState, ScalesetState
from onefuzztypes.models import WorkSet
from onefuzztypes.primitives import Container
from pydantic import BaseModel, Field

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


class Change(BaseModel):
    scalesets: List[Scaleset]
    current_size: int
    change_size: int


class ScalesetChange(BaseModel):
    # scaleset -> new size
    existing: List[Tuple[Scaleset, int]] = Field(default_factory=list)
    # size of each new scaleset
    new_scalesets: List[int] = Field(default_factory=list)


def calc_scaleset_growth(
    pool: Pool, scalesets: List[Scaleset], to_add: int
) -> ScalesetChange:
    config = pool.autoscale
    if not config:
        raise Exception(f"scaling up a non-autoscaling pool: {pool.name}")
    base_size = Scaleset.scaleset_max_size(config.image)

    changes = ScalesetChange()

    for scaleset in sorted(scalesets, key=lambda x: x.scaleset_id):
        if to_add <= 0:
            break

        if scaleset.state not in ScalesetState.can_update():
            continue

        scaleset_max_size = scaleset.max_size()
        if scaleset.size >= scaleset_max_size:
            continue

        scaleset_to_add = min(to_add, scaleset_max_size - scaleset.size)
        changes.existing.append((scaleset, scaleset.size + scaleset_to_add))
        to_add -= scaleset_to_add

    while to_add > 0:
        scaleset_size = min(base_size, to_add)
        changes.new_scalesets.append(scaleset_size)
        to_add -= scaleset_size

    return changes


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
    region = config.region or get_base_region()

    set_shrink_queues(pool, scalesets, 0)

    changes = calc_scaleset_growth(pool, scalesets, to_add)
    for (scaleset, new_size) in changes.existing:
        logging.info(
            AUTOSCALE_LOG_PREFIX
            + "scale up scaleset - pool:%s scaleset:%s "
            + "from:%d to:%d",
            pool.name,
            scaleset.scaleset_id,
            scaleset.size,
            new_size,
        )
        scaleset.set_size(new_size)

    for size in changes.new_scalesets:
        scaleset = Scaleset.create(
            pool_name=pool.name,
            vm_sku=config.vm_sku,
            image=config.image,
            region=region,
            size=size,
            spot_instances=config.spot_instances,
            ephemeral_os_disks=config.ephemeral_os_disks,
            tags={"pool": pool.name},
        )
        logging.info(
            AUTOSCALE_LOG_PREFIX + "added pool:%s scaleset:%s size:%d",
            pool.name,
            scaleset.scaleset_id,
            scaleset.size,
        )


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
            scaleset.set_state(ScalesetState.shutdown)


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


def calculate_change(
    pool: Pool, scalesets: List[Scaleset], scheduled_worksets: int, in_use_nodes: int
) -> Change:
    if not pool.autoscale:
        raise Exception(f"scaling up a non-autoscaling pool: {pool.name}")

    node_need_estimate = scheduled_worksets + in_use_nodes

    new_size = node_need_estimate
    if pool.autoscale.min_size is not None:
        new_size = max(node_need_estimate, pool.autoscale.min_size)
    if pool.autoscale.max_size is not None:
        new_size = min(new_size, pool.autoscale.max_size)

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
            return Change(scalesets=[], change_size=0, current_size=0)
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

    return Change(
        scalesets=scalesets,
        current_size=current_size,
        change_size=new_size - current_size,
    )


def autoscale_pool(pool: Pool) -> None:
    if not pool.autoscale:
        return

    scalesets = Scaleset.search_by_pool(pool.name)

    scheduled_worksets, in_use_nodes = needed_nodes(pool)

    change = calculate_change(pool, scalesets, scheduled_worksets, in_use_nodes)

    if change.change_size > 0:
        clear_synthetic_worksets(pool)
        scale_up(pool, change.scalesets, change.change_size)
    elif change.change_size < 0:
        clear_synthetic_worksets(pool)
        scale_down(pool, change.scalesets, abs(change.change_size))
        shutdown_empty_scalesets(pool, change.scalesets)
    else:
        shutdown_empty_scalesets(pool, change.scalesets)
