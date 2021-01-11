#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import json
import logging

import azure.functions as func
from onefuzztypes.models import NodeHeartbeatEntry
from pydantic import ValidationError

from ..onefuzzlib.events import get_events
from ..onefuzzlib.pools import Node


def main(msg: func.QueueMessage, dashboard: func.Out[str]) -> None:
    body = msg.get_body()
    logging.info("heartbeat: %s", body)
    raw = json.loads(body)
    try:
        entry = NodeHeartbeatEntry.parse_obj(raw)
        node = Node.get_by_machine_id(entry.node_id)
        if not node:
            logging.error("invalid node id: %s", entry.node_id)
            return
        node.heartbeat = datetime.datetime.utcnow()
        node.save()
    except ValidationError:
        logging.error("invalid node heartbeat: %s", raw)

    events = get_events()
    if events:
        dashboard.set(events)
