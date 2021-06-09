#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import inspect
import logging

WORKERS_DONE = False
REDUCE_LOGGING = False


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


# TODO: Replace this with a better method for filtering out logging
# See https://github.com/Azure/azure-functions-python-worker/issues/743
def reduce_logging() -> None:
    global REDUCE_LOGGING
    if REDUCE_LOGGING:
        return

    to_quiet = [
        "azure",
        "cli",
        "grpc",
        "concurrent",
        "oauthlib",
        "msrest",
        "opencensus",
        "urllib3",
        "requests",
        "aiohttp",
        "asyncio",
        "adal-python",
    ]

    for name in logging.Logger.manager.loggerDict:
        logger = logging.getLogger(name)
        for prefix in to_quiet:
            if logger.name.startswith(prefix):
                logger.level = logging.WARN

    REDUCE_LOGGING = True
