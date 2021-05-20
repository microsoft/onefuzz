#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func

from ..onefuzzlib.events import get_events
from ..onefuzzlib.webhooks import WebhookMessageLog
from ..onefuzzlib.workers.scalesets import Scaleset


def main(mytimer: func.TimerRequest, dashboard: func.Out[str]) -> None:  # noqa: F841
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
