#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
import os
from typing import Dict

import azure.functions as func

from ..onefuzzlib.dashboard import get_event
from ..onefuzzlib.notifications.main import new_files


def file_added(event: Dict) -> None:
    parts = event["data"]["url"].split("/")[3:]
    container = parts[0]
    path = "/".join(parts[1:])
    logging.info("file added container: %s - path: %s", container, path)
    new_files(container, path)


def main(msg: func.QueueMessage, dashboard: func.Out[str]) -> None:
    event = json.loads(msg.get_body())

    if event["topic"] != os.environ["ONEFUZZ_DATA_STORAGE"]:
        return

    if event["eventType"] != "Microsoft.Storage.BlobCreated":
        return

    file_added(event)

    event = get_event()
    if event:
        dashboard.set(event)
