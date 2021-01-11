#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json

import azure.functions as func

from ..onefuzzlib.events import get_events
from ..onefuzzlib.updates import Update, execute_update


def main(msg: func.QueueMessage, dashboard: func.Out[str]) -> None:
    body = msg.get_body()
    update = Update.parse_obj(json.loads(body))
    execute_update(update)

    events = get_events()
    if events:
        dashboard.set(events)
