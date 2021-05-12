#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging

import azure.functions as func
from onefuzztypes.models import ProxyHeartbeat

from ..onefuzzlib.events import get_events
from ..onefuzzlib.proxy import PROXY_LOG_PREFIX, Proxy


def main(msg: func.QueueMessage, dashboard: func.Out[str]) -> None:
    body = msg.get_body()
    logging.info(PROXY_LOG_PREFIX + "heartbeat: %s", body)
    raw = json.loads(body)
    heartbeat = ProxyHeartbeat.parse_obj(raw)
    proxy = Proxy.get(heartbeat.region, heartbeat.proxy_id)
    if proxy is None:
        logging.warning(
            PROXY_LOG_PREFIX + "received heartbeat for missing proxy: %s", body
        )
        return
    proxy.heartbeat = heartbeat
    proxy.save()

    events = get_events()
    if events:
        dashboard.set(events)
