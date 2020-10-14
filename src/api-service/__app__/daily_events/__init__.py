#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging

import azure.functions as func
from onefuzztypes.enums import VmState

from ..onefuzzlib.pools import Scaleset
from ..onefuzzlib.proxy import Proxy


def main(mytimer: func.TimerRequest) -> None:  # noqa: F841
    for proxy in Proxy.search():
        if not proxy.is_used():
            logging.info("stopping proxy")
            proxy.state = VmState.stopping
            proxy.save()

    scalesets = Scaleset.search()
    for scaleset in scalesets:
        logging.info("updating scaleset configs: %s", scaleset.scaleset_id)
        scaleset.update_configs()
