#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import datetime
import json
import logging

import azure.functions as func
from onefuzztypes.events import EventNodeHeartbeat
from onefuzztypes.models import NodeHeartbeatEntry
from pydantic import ValidationError

from ..onefuzzlib.events import send_event
from ..onefuzzlib.workers.nodes import Node


def main(msg: func.QueueMessage) -> None:
    body = msg.get_body()
    logging.info("heartbeat: %s", body)
    raw = json.loads(body)
    try:
        entry = NodeHeartbeatEntry.parse_obj(raw)
        node = Node.get_by_machine_id(entry.node_id)
        if not node:
            logging.warning("invalid node id: %s", entry.node_id)
            return
        node.heartbeat = datetime.datetime.utcnow()
        node.save()
        send_event(
            EventNodeHeartbeat(
                machine_id=node.machine_id,
                scaleset_id=node.scaleset_id,
                pool_name=node.pool_name,
                machine_state = node.state
            )
        )
    except ValidationError:
        logging.error("invalid node heartbeat: %s", raw)
