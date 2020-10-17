#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import logging
from datetime import datetime

import azure.functions as func
from onefuzztypes.models import Error, TaskHeartbeatEntry
from pydantic import ValidationError

from ..onefuzzlib.dashboard import get_event
from ..onefuzzlib.tasks.main import Task


def main(msg: func.QueueMessage, dashboard: func.Out[str]) -> None:
    body = msg.get_body()
    logging.info("heartbeat: %s", body)

    raw = json.loads(body)
    try:
        entry = TaskHeartbeatEntry.parse_obj(raw)
        task = Task.get_by_task_id(entry.task_id)
        if isinstance(task, Error):
            logging.error(task)
            return
        if task:
            task.heartbeat = datetime.utcnow()
            task.save()
    except ValidationError:
        logging.error("invalid task heartbat: %s", raw)

    event = get_event()
    if event:
        dashboard.set(event)
