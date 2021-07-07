#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.enums import NodeState, PoolState

from ..onefuzzlib.events import get_events
from ..onefuzzlib.orm import process_state_updates
from ..onefuzzlib.workers.autoscale import autoscale_pool
from ..onefuzzlib.workers.nodes import Node
from ..onefuzzlib.workers.pools import Pool
from ..onefuzzlib.workers.scalesets import Scaleset


def process_scaleset(scaleset: Scaleset) -> None:
    logging.debug("checking scaleset for updates: %s", scaleset.scaleset_id)

    scaleset.update_configs()

    # if the scaleset is touched during cleanup, don't continue to process it
    if scaleset.cleanup_nodes():
        logging.debug("scaleset needed cleanup: %s", scaleset.scaleset_id)
        return

    scaleset.sync_scaleset_size()

    process_state_updates(scaleset)


def main(mytimer: func.TimerRequest, dashboard: func.Out[str]) -> None:  # noqa: F841
    # NOTE: Update pools first, such that scalesets impacted by pool updates
    # (such as shutdown or resize) happen during this iteration `timer_worker`
    # rather than the following iteration.

    pools = Pool.search()
    for pool in pools:
        if pool.state in PoolState.needs_work():
            logging.info("update pool: %s (%s)", pool.pool_id, pool.name)
            process_state_updates(pool)

        if pool.state in PoolState.available() and pool.autoscale:
            autoscale_pool(pool)

    # NOTE: Nodes, and Scalesets should be processed in a consistent order such
    # during 'pool scale down' operations. This means that pools that are
    # scaling down will more likely remove from the same scalesets over time.
    # By more likely removing from the same scalesets, we are more likely to
    # get to empty scalesets, which can safely be deleted.

    Node.mark_outdated_nodes()
    Node.cleanup_busy_nodes_without_work()
    nodes = Node.search_states(states=NodeState.needs_work())
    for node in sorted(nodes, key=lambda x: x.machine_id):
        logging.info("update node: %s", node.machine_id)
        process_state_updates(node)

    scalesets = Scaleset.search()
    for scaleset in sorted(scalesets, key=lambda x: x.scaleset_id):
        process_scaleset(scaleset)

    events = get_events()
    if events:
        dashboard.set(events)
