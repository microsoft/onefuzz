#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import inspect
import logging

WORKERS_DONE = False


def allow_more_workers() -> None:
    global WORKERS_DONE
    if WORKERS_DONE:
        return

    stack = inspect.stack()
    for entry in stack:
        if entry.filename.endswith("azure_functions_worker/dispatcher.py"):
            if entry.frame.f_locals["self"]._sync_call_tp._max_workers == 1:
                logging.info("bumped thread worker count to 50")
                entry.frame.f_locals["self"]._sync_call_tp._max_workers = 50

    WORKERS_DONE = True
