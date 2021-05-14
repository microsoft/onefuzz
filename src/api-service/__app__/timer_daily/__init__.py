#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.enums import VmState
from onefuzztypes.events import EventProxyCreated

from ..onefuzzlib.events import get_events, send_event
from ..onefuzzlib.proxy import Proxy
from ..onefuzzlib.webhooks import WebhookMessageLog
from ..onefuzzlib.workers.scalesets import Scaleset


def main(mytimer: func.TimerRequest, dashboard: func.Out[str]) -> None:  # noqa: F841
    for proxy in Proxy.search():
        if (
            proxy.is_outdated()
            and len(Proxy.search(query={"region": [proxy.region], "outdated": [False]}))
            == 0
        ):
            logging.info("outdated proxy, creating new one.")
            new_proxy = Proxy(region=proxy.region)
            new_proxy.save()
            send_event(EventProxyCreated(region=proxy.region, proxy_id=proxy.proxy_id))
        if not proxy.is_used:
            logging.info("stopping proxy")
            proxy.state = VmState.stopping
            proxy.save()

    scalesets = Scaleset.search()
    for scaleset in scalesets:
        logging.info("updating scaleset configs: %s", scaleset.scaleset_id)
        scaleset.needs_config_update = True
        scaleset.save()

    expired_webhook_logs = WebhookMessageLog.search_expired()
    for log_entry in expired_webhook_logs:
        logging.info(
            "stopping expired webhook message log: %s:%s",
            log_entry.webhook_id,
            log_entry.event_id,
        )
        log_entry.delete()

    events = get_events()
    if events:
        dashboard.set(events)
