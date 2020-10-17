#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json

import azure.functions as func

from ..onefuzzlib.dashboard import get_event
from ..onefuzzlib.updates import Update, execute_update


def main(msg: func.QueueMessage, dashboard: func.Out[str]) -> None:
    body = msg.get_body()
    update = Update.parse_obj(json.loads(body))
    execute_update(update)

    event = get_event()
    if event:
        dashboard.set(event)
