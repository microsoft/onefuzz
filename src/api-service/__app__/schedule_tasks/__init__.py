#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func

from ..onefuzzlib.dashboard import get_event
from ..onefuzzlib.tasks.scheduler import schedule_tasks


def main(mytimer: func.TimerRequest, dashboard: func.Out[str]) -> None:  # noqa: F841
    logging.info("scheduling waiting tasks")

    schedule_tasks()

    event = get_event()
    if event:
        dashboard.set(event)
