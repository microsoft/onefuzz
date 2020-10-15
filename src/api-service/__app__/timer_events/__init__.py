#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.enums import NodeState, PoolState

from ..onefuzzlib.dashboard import get_event
from ..onefuzzlib.orm import process_update
from ..onefuzzlib.pools import Node, Pool


def main(mytimer: func.TimerRequest, dashboard: func.Out[str]) -> None:  # noqa: F841
    pools = Pool.search_states(states=PoolState.needs_work())
    for pool in pools:
        logging.info("update pool: %s (%s)", pool.pool_id, pool.name)
        process_update(pool)

    nodes = Node.search_states(states=NodeState.needs_work())
    for node in nodes:
        logging.info("update node: %s", node.machine_id)
        process_update(node)

    event = get_event()
    if event:
        dashboard.set(event)
