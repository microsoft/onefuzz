#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging

import azure.functions as func

from ..onefuzzlib.dashboard import get_event
from ..onefuzzlib.heartbeat import NodeHeartbeat, TaskHeartbeat


def main(msg: func.QueueMessage, dashboard: func.Out[str]) -> None:
    body = msg.get_body()
    logging.info("heartbeat: %s", body)

    raw = json.loads(body)

    if TaskHeartbeat.try_add(raw):
        pass
    elif NodeHeartbeat.try_add(raw):
        pass

    event = get_event()
    if event:
        dashboard.set(event)
