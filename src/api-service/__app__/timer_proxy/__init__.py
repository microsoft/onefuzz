#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.enums import VmState

from ..onefuzzlib.events import get_events
from ..onefuzzlib.orm import process_state_updates
from ..onefuzzlib.proxy import Proxy


def main(mytimer: func.TimerRequest, dashboard: func.Out[str]) -> None:  # noqa: F841
    # Reminder, proxies are created on-demand.  If something is "wrong" with
    # a proxy, the plan is: delete and recreate it.
    for proxy in Proxy.search():
        if not proxy.is_alive():
            logging.error("proxy alive check failed, stopping: %s", proxy.region)
            proxy.state = VmState.stopping
            proxy.save()
        else:
            proxy.save_proxy_config()

        if proxy.state in VmState.needs_work():
            logging.info("update proxy vm: %s", proxy.region)
            process_state_updates(proxy)

    events = get_events()
    if events:
        dashboard.set(events)
