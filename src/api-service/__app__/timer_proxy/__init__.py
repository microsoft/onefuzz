#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.enums import VmState

from ..onefuzzlib.events import get_events
from ..onefuzzlib.orm import process_state_updates
from ..onefuzzlib.proxy import PROXY_LOG_PREFIX, Proxy
from ..onefuzzlib.workers.scalesets import Scaleset


def main(mytimer: func.TimerRequest, dashboard: func.Out[str]) -> None:  # noqa: F841
    proxies = Proxy.search()
    for proxy in proxies:
        if proxy.state in VmState.available():
            # Note, outdated checked at the start, but set at the end of this loop.
            # As this function is called via a timer, this works around a user
            # requesting to use the proxy while this function is checking if it's
            # out of date
            if proxy.outdated and not proxy.is_used():
                proxy.set_state(VmState.stopping)
            # If something is "wrong" with a proxy, delete & recreate it
            elif not proxy.is_alive():
                logging.error(
                    PROXY_LOG_PREFIX + "alive check failed, stopping: %s", proxy.region
                )
                proxy.set_state(VmState.stopping)
            else:
                proxy.save_proxy_config()

        if proxy.state in VmState.needs_work():
            logging.info(
                PROXY_LOG_PREFIX + "update state. proxy:%s state:%s",
                proxy.region,
                proxy.state,
            )
            process_state_updates(proxy)

        if proxy.state != VmState.stopped and proxy.is_outdated():
            proxy.outdated = True
            proxy.save()

    # make sure there is a proxy for every currently active region
    scalesets = Scaleset.search()
    regions = set(x.region for x in scalesets)
    for region in regions:
        if all(x.outdated for x in proxies if x.region == region):
            Proxy.get_or_create(region)

    events = get_events()
    if events:
        dashboard.set(events)
