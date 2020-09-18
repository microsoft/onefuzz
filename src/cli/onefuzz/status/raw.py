#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import time
from typing import Any, List

from six.moves import input  # workaround for static analysis

from .signalr import Stream

SIGNALR_CONNECT_TIMEOUT_SECONDS = 0.1


def log_entry(onefuzz: Any, entries: List[Any]) -> None:
    for entry in entries:
        onefuzz.logger.info("%s", entry)


def raw(onefuzz: Any, logger: logging.Logger) -> None:
    client = Stream(onefuzz, logger)
    client.setup(lambda x: log_entry(onefuzz, x))

    while client.connected is None:
        time.sleep(SIGNALR_CONNECT_TIMEOUT_SECONDS)

    wait_for_exit()
    client.stop()


def wait_for_exit() -> None:
    text = ""
    while "exit" not in text:
        print("type exit to stop the log stream")
        text = input("")
