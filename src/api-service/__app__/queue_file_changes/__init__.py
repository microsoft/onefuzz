#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
from typing import Dict

import azure.functions as func

from ..onefuzzlib.azure.storage import corpus_accounts
from ..onefuzzlib.events import get_events
from ..onefuzzlib.notifications.main import new_files

# The number of time the function will be retried if an error occurs
# https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-queue-trigger?tabs=csharp#poison-messages
MAX_DEQUEUE_COUNT = 5


def file_added(event: Dict, fail_task_on_transient_error: bool) -> None:
    parts = event["data"]["url"].split("/")[3:]
    container = parts[0]
    path = "/".join(parts[1:])
    logging.info("file added container: %s - path: %s", container, path)
    new_files(container, path, fail_task_on_transient_error)


def main(msg: func.QueueMessage, dashboard: func.Out[str]) -> None:
    event = json.loads(msg.get_body())
    last_try = msg.dequeue_count == MAX_DEQUEUE_COUNT
    # check type first before calling Azure APIs
    if event["eventType"] != "Microsoft.Storage.BlobCreated":
        return

    if event["topic"] not in corpus_accounts():
        return

    file_added(event, last_try)

    events = get_events()
    if events:
        dashboard.set(events)
