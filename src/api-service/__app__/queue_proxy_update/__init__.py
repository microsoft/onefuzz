#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging

import azure.functions as func
from onefuzztypes.models import ProxyHeartbeat

from ..onefuzzlib.events import get_events
from ..onefuzzlib.proxy import Proxy


def main(msg: func.QueueMessage, dashboard: func.Out[str]) -> None:
    body = msg.get_body()
    logging.info("proxy heartbeat: %s", body)
    raw = json.loads(body)
    heartbeat = ProxyHeartbeat.parse_obj(raw)
    proxy = Proxy.get(heartbeat.region)
    if proxy is None:
        logging.warning("received heartbeat for missing proxy: %s", body)
        return
    proxy.heartbeat = heartbeat
    proxy.save()

    events = get_events()
    if events:
        dashboard.set(events)
