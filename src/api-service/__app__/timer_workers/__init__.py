#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.enums import NodeState, PoolState

from ..onefuzzlib.autoscale import autoscale_pool
from ..onefuzzlib.dashboard import get_event
from ..onefuzzlib.orm import process_state_updates
from ..onefuzzlib.pools import Node, Pool, Scaleset


def process_scaleset(scaleset: Scaleset) -> None:
    logging.debug("checking scaleset for updates: %s", scaleset.scaleset_id)

    # if the scaleset is touched during cleanup, don't continue to process it
    if scaleset.cleanup_nodes():
        logging.debug("scaleset needed cleanup: %s", scaleset.scaleset_id)
        return

    process_state_updates(scaleset)


def main(mytimer: func.TimerRequest, dashboard: func.Out[str]) -> None:  # noqa: F841
    Node.mark_outdated_nodes()
    nodes = Node.search_states(states=NodeState.needs_work())
    for node in nodes:
        logging.info("update node: %s", node.machine_id)
        process_state_updates(node)

    scalesets = Scaleset.search()
    for scaleset in scalesets:
        process_scaleset(scaleset)

    pools = Pool.search()
    for pool in pools:
        if pool.state in PoolState.needs_work():
            logging.info("update pool: %s (%s)", pool.pool_id, pool.name)
            process_state_updates(pool)
        elif pool.state in PoolState.available() and pool.autoscale:
            autoscale_pool(pool)

    event = get_event()
    if event:
        dashboard.set(event)
