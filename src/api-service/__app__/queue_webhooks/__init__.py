#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json

import azure.functions as func

from ..onefuzzlib.events import get_events
from ..onefuzzlib.webhooks import WebhookMessageLog, WebhookMessageQueueObj


def main(msg: func.QueueMessage, dashboard: func.Out[str]) -> None:
    body = msg.get_body()
    obj = WebhookMessageQueueObj.parse_obj(json.loads(body))
    WebhookMessageLog.process_from_queue(obj)

    events = get_events()
    if events:
        dashboard.set(events)
